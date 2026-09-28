using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PriestessPet
{
    internal static class SetupLocalAi
    {
        private static int Main(string[] args)
        {
            if(args.Length!=1){Console.Error.WriteLine("Usage: SetupLocalAi.exe <AI folder>");return 1;}
            using(CancellationTokenSource stop=new CancellationTokenSource())
            {
                Console.CancelKeyPress+=delegate(object sender,ConsoleCancelEventArgs e){e.Cancel=true;stop.Cancel();};
                try{Run(args[0],stop.Token).GetAwaiter().GetResult();return 0;}
                catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
            }
        }
        private static async Task Run(string root,CancellationToken token)
        {
            using(LocalAiService service=new LocalAiService(root))
            using(HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}))
            {
                http.BaseAddress=service.Endpoint;http.Timeout=TimeSpan.FromMinutes(30);
                Console.WriteLine("Starting the portable local model service...");
                await service.EnsureServerAsync(token).ConfigureAwait(false);
                if(await HasModelAsync(http,service.Model,token).ConfigureAwait(false))
                    Console.WriteLine("Using the already downloaded "+service.Model+" weights.");
                else using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Post,"api/pull"))
                {
                    Console.WriteLine("Downloading "+service.Model+" (about 3.3 GB) for local chat and attached-image understanding...");
                    request.Content=new StringContent(new JavaScriptSerializer().Serialize(new{model=service.Model,stream=true}),Encoding.UTF8,"application/json");
                    using(HttpResponseMessage response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        using(Stream stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using(StreamReader reader=new StreamReader(stream))
                        using(token.Register(delegate{stream.Dispose();}))
                        {
                            string line,lastStatus="";int lastPercent=-10;
                            while((line=await reader.ReadLineAsync().ConfigureAwait(false))!=null)
                            {
                                token.ThrowIfCancellationRequested();
                                Dictionary<string,object> entry=new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string,object>;
                                if(entry==null)continue;
                                if(entry.ContainsKey("error"))throw new InvalidOperationException(Convert.ToString(entry["error"]));
                                string status=entry.ContainsKey("status")?Convert.ToString(entry["status"]):"Downloading";
                                if(status!=lastStatus){Console.WriteLine(status);lastStatus=status;lastPercent=-10;}
                                if(entry.ContainsKey("completed")&&entry.ContainsKey("total")&&Convert.ToDouble(entry["total"])>0)
                                {
                                    int percent=(int)(100*Convert.ToDouble(entry["completed"])/Convert.ToDouble(entry["total"]));
                                    if(percent>=lastPercent+10){Console.WriteLine("  "+percent+"%");lastPercent=percent;}
                                }
                            }
                        }
                    }
                }
                await service.EnsureReadyAsync(token).ConfigureAwait(false);
                Console.WriteLine("Model installed. Testing a real local reply...");
                string body=new JavaScriptSerializer().Serialize(new {
                    model=service.Model,messages=new[]{
                        new {role="system",content="You are Priestess, a friendly desktop companion. Reply in one short sentence."},
                        new {role="user",content="Say hello and tell me you are ready for a chat."}},
                    think=false,stream=false,keep_alive="2m",
                    options=new {num_ctx=4096,num_predict=180,temperature=0.7,top_p=0.8,top_k=20,min_p=0.0}
                });
                using(StringContent content=new StringContent(body,Encoding.UTF8,"application/json"))
                using(HttpResponseMessage reply=await http.PostAsync("api/chat",content,token).ConfigureAwait(false))
                {
                    reply.EnsureSuccessStatusCode();
                    string json=await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
                    File.WriteAllText(Path.Combine(root,"local-model-last-attempt.json"),json);
                    Dictionary<string,object> result=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
                    Dictionary<string,object> message=result!=null&&result.ContainsKey("message")?result["message"] as Dictionary<string,object>:null;
                    if(result==null||!result.ContainsKey("done")||!Object.Equals(result["done"],true)||
                        !result.ContainsKey("done_reason")||Convert.ToString(result["done_reason"])!="stop")
                        throw new InvalidOperationException("The local model did not complete its test reply. Setup has not passed verification.");
                    if(message==null||!message.ContainsKey("content")||String.IsNullOrWhiteSpace(Convert.ToString(message["content"])))throw new InvalidOperationException("The model returned an empty reply.");
                    if(message.ContainsKey("thinking")&&!String.IsNullOrWhiteSpace(Convert.ToString(message["thinking"])))
                        throw new InvalidOperationException("The local model returned reasoning instead of a direct chat reply.");
                    if(Convert.ToString(message["content"]).Contains("<think>")||Convert.ToString(message["content"]).Contains("</think>"))
                        throw new InvalidOperationException("The local model's direct chat template was not applied.");
                    Console.WriteLine("Local reply: "+Convert.ToString(message["content"]));
                    File.WriteAllText(Path.Combine(root,"local-model-verification.json"),json);
                }
                await VerifyVisionAsync(http,service.Model,root,token).ConfigureAwait(false);
                Console.WriteLine("Local AI is ready. Runtime, model and temporary files are in "+Path.GetFullPath(root));
            }
        }

        // This test creates its own simple image; setup never captures the desktop.
        private static async Task VerifyVisionAsync(HttpClient http,string model,string root,CancellationToken token)
        {
            Console.WriteLine("Checking image reading with a generated test card (no screen capture)...");
            string image;
            using(Bitmap card=new Bitmap(1000,560))
            using(Graphics graphics=Graphics.FromImage(card))
            using(Font heading=new Font("Segoe UI",32,FontStyle.Bold))
            using(Font code=new Font("Segoe UI",52,FontStyle.Bold))
            using(MemoryStream bytes=new MemoryStream())
            {
                graphics.Clear(Color.White);
                graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("PRIESTESS VISION CHECK",heading,Brushes.Black,30,25);
                graphics.DrawString("CODE: MAPLE 742",code,Brushes.Black,30,155);
                for(int i=0;i<3;i++) graphics.FillEllipse(Brushes.Red,35+i*145,350,95,95);
                graphics.FillRectangle(Brushes.Blue,735,350,95,95);
                card.Save(bytes,ImageFormat.Png);
                image=Convert.ToBase64String(bytes.ToArray());
            }
            string body=new JavaScriptSerializer { MaxJsonLength=4*1024*1024 }.Serialize(new {
                model=model,
                messages=new[]{new{role="user",content="Read the code printed after CODE in this image. Then state the number and color of the circles. Reply in one short sentence.",images=new[]{image}}},
                stream=false,keep_alive=0,
                options=new{num_ctx=4096,num_predict=120,temperature=0.0}
            });
            Stopwatch elapsed=Stopwatch.StartNew();
            using(StringContent content=new StringContent(body,Encoding.UTF8,"application/json"))
            using(HttpResponseMessage reply=await http.PostAsync("api/chat",content,token).ConfigureAwait(false))
            {
                reply.EnsureSuccessStatusCode();
                string json=await reply.Content.ReadAsStringAsync().ConfigureAwait(false);
                File.WriteAllText(Path.Combine(root,"local-vision-last-attempt.json"),json);
                Dictionary<string,object> result=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
                Dictionary<string,object> message=result!=null&&result.ContainsKey("message")?result["message"] as Dictionary<string,object>:null;
                string answer=message!=null&&message.ContainsKey("content")?Convert.ToString(message["content"]):"";
                string normalized=answer.ToUpperInvariant();
                if(result==null||!result.ContainsKey("done_reason")||Convert.ToString(result["done_reason"])!="stop"||
                    !normalized.Contains("MAPLE")||!normalized.Contains("742")||!normalized.Contains("RED")||
                    !(normalized.Contains("THREE")||normalized.Contains("3")))
                    throw new InvalidOperationException("The vision model did not read the test card correctly: "+answer);
                Console.WriteLine("Image reply ("+elapsed.Elapsed.TotalSeconds.ToString("F1")+" s): "+answer);
                File.WriteAllText(Path.Combine(root,"local-vision-verification.json"),json);
            }
        }

        private static async Task<bool> HasModelAsync(HttpClient http,string nameToFind,CancellationToken token)
        {
            using(HttpResponseMessage response=await http.GetAsync("api/tags",token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                Dictionary<string,object> data=new JavaScriptSerializer().DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)) as Dictionary<string,object>;
                object models;
                if(data==null||!data.TryGetValue("models",out models))return false;
                object[] list=models as object[];
                if(list==null)return false;
                foreach(object item in list)
                {
                    Dictionary<string,object> model=item as Dictionary<string,object>;object name;
                    if(model!=null&&model.TryGetValue("name",out name)&&Convert.ToString(name)==nameToFind&&
                        !model.ContainsKey("remote_host")&&!model.ContainsKey("remote_model"))return true;
                }
                return false;
            }
        }
    }
}
