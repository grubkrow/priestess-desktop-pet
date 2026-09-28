using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Priestess Desktop Pet")]
[assembly: System.Reflection.AssemblyDescription("A portable Priestess Shimeji-style desktop companion")]
[assembly: System.Reflection.AssemblyVersion("8.0.0.0")]

namespace PriestessPet
{
    internal static class Program
    {
        internal static readonly int RecallMessage=(int)RegisterWindowMessage("PriestessPet.Recall.v1");
        [STAThread]
        private static int Main(string[] args)
        {
            LayeredPetWindow.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if(args.Length>0 && args[0]=="--self-test")
                {
                    string folder=args.Length>1?Path.GetFullPath(args[1]):AppDomain.CurrentDomain.BaseDirectory;
                    Directory.CreateDirectory(folder);
                    using(SpriteAtlas atlas=new SpriteAtlas())
                    {
                        StringBuilder report=new StringBuilder();
                        report.AppendLine(atlas.Validate());
                        report.AppendLine(AiAnimation.Validate(atlas));
                        report.AppendLine(MotionTests.Run());
                        report.AppendLine(CareTests.Run(Path.Combine(folder,"care-tests")));
                        report.AppendLine(NativeWindowTests.Run(atlas));
                        report.AppendLine(WindowPerchTests.Run());
                        report.AppendLine(LocalChatTests.Run());
                        report.AppendLine(ChatInputTests.Run(atlas));
                        report.AppendLine(ContextFilesTests.Run());
                        report.AppendLine(AttachmentUiTests.Run(atlas,folder));
                        report.AppendLine(DesktopFeatureTests.Run(atlas,folder));
                        atlas.RenderContactSheet(Path.Combine(folder,"sprite-render-preview.png"));
                        atlas.RenderContactSheet(Path.Combine(folder,"sprite-render-light.png"),true);
                        atlas.WriteMetrics(Path.Combine(folder,"sprite-alignment.txt"));
                        AnimationPreview.Render(atlas,Path.Combine(folder,"animation-preview.gif"));
                        AiAnimation.RenderContactSheet(atlas,Path.Combine(folder,"ai-sprites-light.png"),true);
                        AiAnimation.RenderContactSheet(atlas,Path.Combine(folder,"ai-sprites-dark.png"),false);
                        AiAnimation.RenderPreview(atlas,Path.Combine(folder,"ai-animation-preview.gif"));
                        AiAnimation.ExportSheet(atlas,Path.Combine(folder,"ai-sprite-sheet.png"));
                        using(CarePanel carePreview=new CarePanel(atlas,new PetCare(),delegate{},delegate(FoodKind food){}))
                            carePreview.SavePreview(Path.Combine(folder,"care-panel-preview.png"));
                        File.WriteAllText(Path.Combine(folder,"test-results.txt"),report.ToString());
                    }
                    return 0;
                }
                bool acquired;
                using(Mutex single=new Mutex(true,InstanceMutexName(),out acquired))
                {
                    if(!acquired)
                    {
                        PostMessage(new IntPtr(0xffff),RecallMessage,IntPtr.Zero,IntPtr.Zero);
                        return 0;
                    }
                    try
                    {
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){ShowError(e.Exception);Application.ExitThread();};
                        bool isSmoke=args.Length>0&&args[0]=="--smoke-test";
                        string smokePath=isSmoke?(args.Length>1?Path.GetFullPath(args[1]):Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"checks")):null;
                        using(SpriteAtlas atlas=new SpriteAtlas())
                        using(PetWindow pet=new PetWindow(atlas,isSmoke?Path.Combine(smokePath,"smoke-care.xml"):null,isSmoke))
                        {
                            if(isSmoke)pet.ConfigureSmokeTest(smokePath);
                            if(args.Length>0&&args[0]=="--chat")pet.Shown+=delegate{pet.ShowChatPanel();};
                            Application.Run(pet);
                        }
                    }
                    finally {single.ReleaseMutex();}
                }
                return 0;
            }
            catch(Exception e)
            {
                if(args.Length>0 && args[0]=="--self-test")
                {
                    string folder=args.Length>1?Path.GetFullPath(args[1]):AppDomain.CurrentDomain.BaseDirectory;
                    Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"test-failure.txt"),e.ToString());
                }
                else ShowError(e);
                return 1;
            }
        }
        private static void ShowError(Exception e)
        {
            string log=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"priestess-error.log");
            try{File.WriteAllText(log,e.ToString());}catch{log="(The error log could not be saved.)";}
            MessageBox.Show("Priestess could not continue.\n\n"+e.Message+"\n\nDetails: "+log,"Priestess Desktop Pet",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
        private static string InstanceMutexName()
        {
            string folder=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant();
            using(SHA256 sha=SHA256.Create())
            {
                byte[] digest=sha.ComputeHash(Encoding.UTF8.GetBytes(folder));
                StringBuilder hex=new StringBuilder(digest.Length*2);
                foreach(byte value in digest)hex.Append(value.ToString("X2"));
                return "Local\\PriestessDesktopPet."+hex.ToString();
            }
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string text);
        [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
    }

    public sealed class PetWindow : LayeredPetWindow
    {
        private readonly SpriteAtlas atlas;
        private readonly PetMotion motion;
        private readonly PetCare care;
        private readonly string carePath;
        private readonly CareEffects effects=new CareEffects();
        private CareFeedback feedback;
        private CarePanel carePanel;
        private ChatPanel chatPanel;
        private ChatActivity chatActivity=ChatActivity.Idle;
        private double chatActivityAge,chatActivityLifetime;
        private readonly LocalAiService localAi=new LocalAiService();
        private readonly WindowPerch windowPerch;
        private WindowSupport windowSupport;
        private double perchRefreshAge,ignoreWindowsAge;
        private bool returningToDesktop;
        private readonly ToolStripMenuItem perchItem=new ToolStripMenuItem("Land on app windows");
        private CareReaction reaction=CareReaction.None,pendingReaction=CareReaction.None;
        private FoodKind reactionFood;
        private double reactionAge,feedbackAge,feedbackLifetime,saveAge,careRefreshAge;
        private string feedbackMessage="",pendingMessage="";
        private bool feedbackHearts,pointerDown,pressedHead,lastSaveFailed;
        private Point pointerStart;
        private readonly ToolStripMenuItem careStatusItem=new ToolStripMenuItem();
        private readonly ToolStripMenuItem foodMenu=new ToolStripMenuItem("Feed a snack");
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly ContextMenuStrip menu=new ContextMenuStrip();
        private readonly NotifyIcon tray=new NotifyIcon();
        private readonly Icon petIcon;
        private readonly ToolStripMenuItem pauseItem=new ToolStripMenuItem("Pause movement");
        private readonly ToolStripMenuItem topItem=new ToolStripMenuItem("Always on top");
        private readonly ToolStripMenuItem monitorMenu=new ToolStripMenuItem("Move to monitor");
        private Screen currentScreen;
        private Point dragOffset;
        private bool dragging,menuOpen,closing,cleanedUp;
        private double previousTime;
        private int boundsTicks;
        private string smokeFolder;
        private int smokeStep=-1;
        private double smokeTime;

        public PetWindow(SpriteAtlas spriteAtlas,string saveFile=null,bool freshCare=false,WindowPerch perchSource=null)
        {
            atlas=spriteAtlas;
            windowPerch=perchSource??new WindowPerch();
            carePath=saveFile??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"priestess-care.xml");
            care=freshCare?new PetCare():PetCare.Load(carePath);
            Text="Priestess Desktop Pet";
            currentScreen=Screen.FromPoint(Cursor.Position);
            motion=new PetMotion(currentScreen.WorkingArea,180,Environment.TickCount);
            Cursor=Cursors.Hand;
            using(Bitmap iconFrame=new Bitmap(atlas.GetFrame(0,64,false)))
            {
                IntPtr handle=iconFrame.GetHicon();
                try{using(Icon borrowed=Icon.FromHandle(handle))petIcon=(Icon)borrowed.Clone();}
                finally{DestroyIcon(handle);}
            }
            Icon=petIcon;
            BuildMenu();
            tray.Icon=petIcon;
            tray.Text="Priestess — right-click for controls";
            tray.ContextMenuStrip=menu;
            tray.MouseDoubleClick+=delegate(object s,MouseEventArgs e){RecallPet();};
            tray.Visible=true;
            timer.Interval=15;
            timer.Tick+=Tick;
            SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            PresentFrame();
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);PresentFrame();previousTime=clock.Elapsed.TotalSeconds;timer.Start();
        }
        private void BuildMenu()
        {
            menu.Items.Add(new ToolStripMenuItem("Priestess  ·  Arknights"){Enabled=false});
            careStatusItem.Enabled=false;menu.Items.Add(careStatusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Care && snacks...",null,delegate{ShowCarePanel();});
            menu.Items.Add("Chat with Priestess...",null,delegate{ShowChatPanel();});
            menu.Items.Add("Pat her head",null,delegate{PatPet();});
            foreach(FoodKind choice in Enum.GetValues(typeof(FoodKind)))
            {
                FoodKind selected=choice;
                foodMenu.DropDownItems.Add(PetCare.FoodName(choice),null,delegate{FeedPet(selected);});
            }
            menu.Items.Add(foodMenu);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem actions=new ToolStripMenuItem("Actions");
            AddAction(actions,"Walk",PetState.Walk);
            AddAction(actions,"Sit",PetState.Sit);
            AddAction(actions,"Sleep",PetState.Sleep);
            AddAction(actions,"Wake up",PetState.Idle);
            AddAction(actions,"Climb screen edge",PetState.Climb);
            AddAction(actions,"Hang from ceiling",PetState.Hang);
            AddAction(actions,"Shrug",PetState.Shrug);
            actions.DropDownItems.Add("Try thinking pose",null,delegate{SetChatActivity(ChatActivity.Thinking);chatActivityLifetime=7;});
            actions.DropDownItems.Add("Read with glasses",null,delegate{SetChatActivity(ChatActivity.Reading);chatActivityLifetime=10;});
            menu.Items.Add(actions);
            menu.Items.Add("Sit on a window",null,delegate{SitOnWindow();});
            menu.Items.Add("Return to desktop",null,delegate{ReturnToDesktop();});
            perchItem.CheckOnClick=true;perchItem.Checked=true;
            perchItem.Click+=delegate{if(!perchItem.Checked)ReturnToDesktop();};
            menu.Items.Add(perchItem);
            pauseItem.CheckOnClick=true;
            pauseItem.Click+=delegate{motion.Paused=pauseItem.Checked;};
            menu.Items.Add(pauseItem);
            ToolStripMenuItem sizes=new ToolStripMenuItem("Size");
            AddChoice(sizes,"Small · 120 px",120,180,delegate(double value){motion.SetSize((int)value);PresentFrame();});
            AddChoice(sizes,"Medium · 180 px",180,180,delegate(double value){motion.SetSize((int)value);PresentFrame();});
            AddChoice(sizes,"Large · 240 px",240,180,delegate(double value){motion.SetSize((int)value);PresentFrame();});
            menu.Items.Add(sizes);
            ToolStripMenuItem speeds=new ToolStripMenuItem("Speed");
            AddChoice(speeds,"Slow",0.65,1,delegate(double value){motion.SpeedMultiplier=value;});
            AddChoice(speeds,"Normal",1,1,delegate(double value){motion.SpeedMultiplier=value;});
            AddChoice(speeds,"Lively",1.55,1,delegate(double value){motion.SpeedMultiplier=value;});
            menu.Items.Add(speeds);
            topItem.Checked=true;topItem.CheckOnClick=true;
            topItem.Click+=delegate{TopMost=topItem.Checked;};
            menu.Items.Add(topItem);
            menu.Items.Add(monitorMenu);
            menu.Items.Add("Bring Priestess back",null,delegate{RecallPet();});
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("How to play",null,delegate{
                MessageBox.Show("Click her head for a gentle pat.\nDrag to pick her up; release to drop her.\nDouble-click her body for a cheeky shrug.\n\nRight-click > Care & snacks to feed her and see her mood.\nCare progress is saved beside the app.\n\nDrop her onto a window's top edge, or choose Sit on a window.\nLeave room above a restored window for her to sit.\nReturn to desktop lets her drop down again.\n\nChat with Priestess opens her local AI chat.\nThe model starts with your first message.\nClosing chat clears that conversation.\n\nOpen Priestess.exe again to bring her back.\nChoose Quit to close her and her local AI.","Priestess · How to play",MessageBoxButtons.OK,MessageBoxIcon.Information);
            });
            menu.Items.Add("Quit",null,delegate{Close();});
            menu.Opening+=delegate{
                menuOpen=true;pauseItem.Checked=motion.Paused;topItem.Checked=TopMost;
                RefreshCare(null);
                while(monitorMenu.DropDownItems.Count>0)monitorMenu.DropDownItems[0].Dispose();
                Screen[] screens=Screen.AllScreens;
                for(int i=0;i<screens.Length;i++)
                {
                    Screen selected=screens[i];
                    ToolStripMenuItem item=new ToolStripMenuItem("Monitor "+(i+1)+(selected.Primary?" · Primary":""));
                    item.Checked=selected.DeviceName==currentScreen.DeviceName;
                    item.Click+=delegate{
                        CancelCare();pointerDown=false;dragging=false;Capture=false;motion.Paused=false;pauseItem.Checked=false;
                        windowSupport=null;ignoreWindowsAge=1;returningToDesktop=false;
                        currentScreen=selected;motion.SetBounds(selected.WorkingArea);motion.Recall();PresentFrame();
                    };
                    monitorMenu.DropDownItems.Add(item);
                }
            };
            menu.Closed+=delegate{menuOpen=false;previousTime=clock.Elapsed.TotalSeconds;};
        }
        private void AddAction(ToolStripMenuItem parent,string label,PetState state)
        {
            parent.DropDownItems.Add(label,null,delegate{
                CancelCare();pointerDown=false;dragging=false;Capture=false;motion.Paused=false;pauseItem.Checked=false;
                SetChatActivity(ChatActivity.Idle);
                motion.Command(state);PresentFrame();
            });
        }
        private static void AddChoice(ToolStripMenuItem parent,string label,double value,double initial,Action<double> action)
        {
            ToolStripMenuItem item=new ToolStripMenuItem(label){Checked=value==initial};
            item.Click+=delegate{
                foreach(ToolStripItem child in parent.DropDownItems)((ToolStripMenuItem)child).Checked=false;
                item.Checked=true;action(value);
            };
            parent.DropDownItems.Add(item);
        }
        private void Tick(object sender,EventArgs e)
        {
            double now=clock.Elapsed.TotalSeconds,dt=now-previousTime;previousTime=now;
            double activeSeconds=Math.Max(0,Math.Min(dt,0.25));
            care.Update(activeSeconds);
            ignoreWindowsAge=Math.Max(0,ignoreWindowsAge-activeSeconds);
            perchRefreshAge+=activeSeconds;
            if(perchRefreshAge>=0.06){perchRefreshAge=0;RefreshPerch();}
            double previousY=motion.Y;
            bool wasFalling=motion.State==PetState.Fall;
            if(!menuOpen&&reaction==CareReaction.None&&!pointerDown&&!ShowChatPose)motion.Update(dt);
            if(wasFalling&&perchItem.Checked&&ignoreWindowsAge<=0&&!returningToDesktop&&!motion.IsPerched&&motion.Y>previousY)
            {
                WindowSupport landing;
                if(windowPerch.TryFindLanding(motion.Bounds,motion.Size,motion.X,previousY,motion.Y,out landing))AttachWindow(landing);
            }
            if(returningToDesktop&&motion.Y+motion.Size>=motion.Bounds.Bottom-1)returningToDesktop=false;
            if(pendingReaction!=CareReaction.None&&(OnGround||motion.Paused))BeginCareReaction();
            if(reaction!=CareReaction.None)
            {
                reactionAge+=activeSeconds;
                if(reactionAge>=(reaction==CareReaction.Pat?1.6:3.0))reaction=CareReaction.None;
            }
            if(chatActivity!=ChatActivity.Idle)
            {
                if(reaction==CareReaction.None)chatActivityAge+=activeSeconds;
                if(chatActivityAge>=chatActivityLifetime)SetChatActivity(ChatActivity.Idle);
            }
            feedbackAge+=activeSeconds;saveAge+=activeSeconds;careRefreshAge+=activeSeconds;
            if(saveAge>=60){saveAge=0;lastSaveFailed=!care.Save(carePath);}
            if(careRefreshAge>=0.5){careRefreshAge=0;RefreshCare(null);}
            if(++boundsTicks>=60){boundsTicks=0;RefreshBounds();}
            if(smokeFolder!=null)RunSmokeStep(dt);
            if(!closing){PresentFrame();UpdateFeedback();}
        }
        private void PresentFrame()
        {
            Bitmap frame=reaction!=CareReaction.None?effects.Frame(atlas,motion.Size,reaction,reactionAge,reactionFood):
                ShowChatPose?atlas.GetFrame(AiAnimation.Frame(chatActivity==ChatActivity.Reading,chatActivityAge),motion.Size,false):atlas.GetFrame(motion.Frame,motion.Size,motion.FacingLeft);
            Present(frame,(int)Math.Round(motion.X),(int)Math.Round(motion.Y));
        }
        private bool ShowChatPose {get{return chatActivity!=ChatActivity.Idle&&!dragging&&OnGround&&motion.State!=PetState.Fall&&motion.State!=PetState.Drag&&motion.State!=PetState.Climb&&motion.State!=PetState.Hang;}}
        private void SetChatActivity(ChatActivity value)
        {
            if(closing||IsDisposed)return;
            chatActivity=value;chatActivityAge=0;
            chatActivityLifetime=value==ChatActivity.Thinking?Double.PositiveInfinity:12;
            if(value!=ChatActivity.Idle&&OnGround&&!dragging&&!pointerDown)motion.Command(PetState.Idle);
            PresentFrame();
        }
        private void RefreshBounds()
        {
            Screen match=null;
            foreach(Screen s in Screen.AllScreens)if(s.DeviceName==currentScreen.DeviceName){match=s;break;}
            if(match==null){currentScreen=Screen.PrimaryScreen;motion.SetBounds(currentScreen.WorkingArea);motion.Recall();}
            else{currentScreen=match;motion.SetBounds(match.WorkingArea);}
        }
        private void DisplayChanged(object sender,EventArgs e)
        {
            if(closing||IsDisposed||!IsHandleCreated)return;
            try{BeginInvoke((MethodInvoker)delegate{if(!closing){RefreshBounds();PresentFrame();}});}
            catch(InvalidOperationException){}
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Right){menu.Show(Cursor.Position);return;}
            if(e.Button!=MouseButtons.Left)return;
            PointerPressed(Cursor.Position,e.Clicks);
        }
        private void PointerPressed(Point cursor,int clicks)
        {
            int frame=reaction==CareReaction.Pat?32:reaction==CareReaction.Eat?36:ShowChatPose?AiAnimation.Frame(chatActivity==ChatActivity.Reading,chatActivityAge):motion.Frame;
            bool head=atlas.IsHead(frame,motion.Size,reaction==CareReaction.None&&!ShowChatPose&&motion.FacingLeft,new Point(cursor.X-(int)motion.X,cursor.Y-(int)motion.Y));
            if(clicks>=2&&!head)
            {
                CancelCare();pointerDown=false;dragging=false;Capture=false;motion.Paused=false;pauseItem.Checked=false;
                motion.Command(PetState.Shrug);PresentFrame();return;
            }
            pointerStart=cursor;pointerDown=true;dragging=false;
            pressedHead=head;
            Capture=true;
        }
        private void BeginDragging(Point cursor)
        {
            CancelCare();pointerDown=false;windowSupport=null;ignoreWindowsAge=0;returningToDesktop=false;
            dragOffset=new Point(cursor.X-(int)motion.X,cursor.Y-(int)motion.Y);
            dragging=true;Capture=true;motion.BeginDrag();PresentFrame();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);PointerMoved(Cursor.Position);
        }
        private void PointerMoved(Point cursor)
        {
            if(pointerDown&&!dragging)
            {
                int threshold=Math.Max(5,SystemInformation.DragSize.Width/2);
                if(Math.Abs(cursor.X-pointerStart.X)<threshold&&Math.Abs(cursor.Y-pointerStart.Y)<threshold)return;
                BeginDragging(pointerStart);
            }
            if(dragging)MoveDragging(cursor);
        }
        private void MoveDragging(Point cursor)
        {
            Screen target=Screen.FromPoint(cursor);
            if(target.DeviceName!=currentScreen.DeviceName){currentScreen=target;motion.SetBounds(target.WorkingArea);}
            motion.DragTo(cursor.X-dragOffset.X,cursor.Y-dragOffset.Y);PresentFrame();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if(e.Button==MouseButtons.Left)PointerReleased();
        }
        private void PointerReleased()
        {
            if(dragging){EndDragging();return;}
            bool pat=pointerDown&&pressedHead;
            pointerDown=false;Capture=false;
            if(pat)PatPet();
        }
        private void EndDragging()
        {
            pointerDown=false;dragging=false;Capture=false;motion.EndDrag();PresentFrame();
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if(!Capture)pointerDown=false;
            if(dragging&&!Capture){dragging=false;motion.EndDrag();}
        }
        private void RecallPet()
        {
            CancelCare();pointerDown=false;dragging=false;Capture=false;motion.Paused=false;pauseItem.Checked=false;
            windowSupport=null;ignoreWindowsAge=1;returningToDesktop=false;
            currentScreen=Screen.FromPoint(Cursor.Position);motion.SetBounds(currentScreen.WorkingArea);
            motion.Recall();PresentFrame();
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==Program.RecallMessage){RecallPet();return;}
            base.WndProc(ref m);
        }
        private bool OnGround {get{return motion.IsPerched||motion.Y+motion.Size>=motion.Bounds.Bottom-1;}}
        private CareResult PatPet()
        {
            CareResult result=care.Pat();
            if(result.Accepted)QueueCare(CareReaction.Pat,FoodKind.Berry,result.Message);
            else ShowFeedback(result.Message,false);
            SaveCareAndRefresh(result.Message);return result;
        }
        private CareResult FeedPet(FoodKind food)
        {
            CareResult result=care.Feed(food);
            if(result.Accepted)QueueCare(CareReaction.Eat,food,result.Message);
            else ShowFeedback(result.Message,false);
            SaveCareAndRefresh(result.Message);return result;
        }
        private void QueueCare(CareReaction kind,FoodKind food,string message)
        {
            pointerDown=false;dragging=false;Capture=false;
            if(motion.State==PetState.Drag)motion.EndDrag();
            reaction=CareReaction.None;pendingReaction=kind;reactionFood=food;pendingMessage=message;
            motion.Command(PetState.Idle);
            if(OnGround||motion.Paused)BeginCareReaction();
        }
        private void BeginCareReaction()
        {
            if(OnGround)motion.Command(PetState.Idle);
            reaction=pendingReaction;pendingReaction=CareReaction.None;reactionAge=0;
            ShowFeedback(pendingMessage,true);PresentFrame();
        }
        private void CancelCare()
        {
            reaction=CareReaction.None;pendingReaction=CareReaction.None;
            feedbackLifetime=0;if(feedback!=null)feedback.Hide();
        }
        private void ShowFeedback(string message,bool hearts)
        {
            feedbackMessage=message;feedbackHearts=hearts;feedbackAge=0;feedbackLifetime=3;
            UpdateFeedback();
        }
        private void UpdateFeedback()
        {
            if(feedbackAge>=feedbackLifetime){if(feedback!=null)feedback.Hide();return;}
            if(feedback==null)feedback=new CareFeedback();
            feedback.UpdateFeedback(feedbackMessage,feedbackHearts,feedbackAge,new Rectangle((int)motion.X,(int)motion.Y,motion.Size,motion.Size),motion.Bounds,TopMost);
        }
        private void RefreshCare(string message)
        {
            careStatusItem.Text=String.Format("{0} · Fullness {1:0} · Affection {2:0}",care.Mood,care.Fullness,care.Affection);
            tray.Text="Priestess · "+care.Mood+" · Right-click for care";
            if(carePanel!=null&&!carePanel.IsDisposed)carePanel.RefreshCare(message);
        }
        private void SaveCareAndRefresh(string message)
        {
            lastSaveFailed=!care.Save(carePath);
            if(lastSaveFailed)
            {
                message="Progress could not be saved in this folder.";
                ShowFeedback(message,false);
            }
            saveAge=0;RefreshCare(message);
        }
        private void ShowCarePanel()
        {
            if(carePanel==null||carePanel.IsDisposed)
            {
                carePanel=new CarePanel(atlas,care,delegate{PatPet();},delegate(FoodKind food){FeedPet(food);});
                carePanel.Icon=petIcon;
                carePanel.StartPosition=FormStartPosition.Manual;
                Rectangle area=currentScreen.WorkingArea;
                carePanel.Location=new Point(Math.Max(area.Left,Math.Min(area.Right-carePanel.Width,(int)motion.X+motion.Size+14)),Math.Max(area.Top,Math.Min(area.Bottom-carePanel.Height,(int)motion.Y)));
            }
            carePanel.RefreshCare(lastSaveFailed?"Progress could not be saved in this folder.":null);carePanel.Show(this);carePanel.Activate();
        }
        public void ShowChatPanel()
        {
            if(chatPanel==null||chatPanel.IsDisposed)
            {
                chatPanel=new ChatPanel(atlas,care,localAi,delegate(string reply){
                    if(closing||IsDisposed)return;
                    string bubble=reply.Length>105?reply.Substring(0,102)+"...":reply;
                    ShowFeedback(bubble,false);
                    SetChatActivity(ChatActivity.Reading);
                    chatActivityLifetime=AiAnimation.ReadingDuration(reply);
                });
                chatPanel.ActivityChanged+=SetChatActivity;
                chatPanel.FormClosed+=delegate{SetChatActivity(ChatActivity.Idle);};
                chatPanel.Icon=petIcon;
                chatPanel.StartPosition=FormStartPosition.Manual;
                Rectangle area=currentScreen.WorkingArea;
                chatPanel.Location=new Point(Math.Max(area.Left,Math.Min(area.Right-chatPanel.Width,(int)motion.X+motion.Size+14)),Math.Max(area.Top,Math.Min(area.Bottom-chatPanel.Height,(int)motion.Y)));
            }
            if(chatPanel.WindowState==FormWindowState.Minimized)chatPanel.WindowState=FormWindowState.Normal;
            chatPanel.Show(this);chatPanel.Activate();
        }
        private void SitOnWindow()
        {
            WindowSupport support;
            if(!windowPerch.TryFindNearest(motion.Bounds,motion.Size,motion.X,motion.Y,out support))
            {
                ShowFeedback("Move a window lower so I have room to sit on top.",false);return;
            }
            CancelCare();pointerDown=false;dragging=false;Capture=false;
            motion.Paused=false;pauseItem.Checked=false;perchItem.Checked=true;
            AttachWindow(support);ShowFeedback("A cozy spot to keep you company.",false);PresentFrame();
        }
        private void AttachWindow(WindowSupport support)
        {
            returningToDesktop=false;windowSupport=support;motion.AttachPerch(support.X,support.Y);
        }
        private void RefreshPerch()
        {
            if(windowSupport==null)return;
            if(!motion.IsPerched){windowSupport=null;ignoreWindowsAge=0.6;return;}
            Screen screen=Screen.FromHandle(windowSupport.Handle);
            WindowSupport updated;
            if(!windowPerch.TryRefresh(windowSupport,screen.WorkingArea,motion.Size,out updated))
            {
                windowSupport=null;motion.DetachPerch();ignoreWindowsAge=0.6;CancelCare();return;
            }
            currentScreen=screen;motion.SetBounds(screen.WorkingArea);
            windowSupport=updated;motion.MovePerch(updated.X,updated.Y);
        }
        private void ReturnToDesktop()
        {
            CancelCare();windowSupport=null;ignoreWindowsAge=2;returningToDesktop=true;
            pointerDown=false;dragging=false;Capture=false;
            motion.Paused=false;pauseItem.Checked=false;
            motion.DetachPerch();motion.Command(PetState.Fall);PresentFrame();
        }
        internal string VerifyWindowFeatures(Form host,string folder)
        {
            SitOnWindow();
            if(windowSupport==null||windowSupport.Handle!=host.Handle||!motion.IsPerched||motion.State!=PetState.Sit)throw new Exception("Window sit action failed.");
            double oldY=motion.Y;
            host.Location=new Point(host.Left+35,host.Top+20);host.Width-=60;
            Application.DoEvents();RefreshPerch();
            if(!motion.IsPerched||Math.Abs(motion.Y-oldY-20)>1)throw new Exception("Pet did not follow its moving window.");
            SetChatActivity(ChatActivity.Thinking);
            if(!ShowChatPose||!motion.IsPerched||AiAnimation.Frame(false,chatActivityAge)!=40)throw new Exception("Thinking pose detached the window or failed to start.");
            if(!PatPet().Accepted||!motion.IsPerched||reaction!=CareReaction.Pat)throw new Exception("Patting detached the perched pet.");
            SetChatActivity(ChatActivity.Reading);
            if(reaction!=CareReaction.Pat||!motion.IsPerched)throw new Exception("AI reply interrupted a pat or window perch.");
            if(!FeedPet(FoodKind.Berry).Accepted||!motion.IsPerched||reaction!=CareReaction.Eat)throw new Exception("Feeding detached the perched pet.");
            CancelCare();motion.SetSize(120);RefreshPerch();
            if(!motion.IsPerched||Math.Abs(motion.Y+motion.Size*WindowPerch.SoleRatio-windowSupport.WindowBounds.Top)>1)throw new Exception("Resized pet lost window alignment.");
            double landingX=motion.X,landingY=motion.Y;
            windowSupport=null;motion.BeginDrag();motion.DragTo(landingX,landingY-18);motion.EndDrag();
            if(ShowChatPose)throw new Exception("Reading sprite replaced falling physics.");
            SetChatActivity(ChatActivity.Idle);
            for(int i=0;i<12&&!motion.IsPerched;i++)
            {
                double before=motion.Y;motion.Update(0.1);WindowSupport landed;
                if(windowPerch.TryFindLanding(motion.Bounds,motion.Size,motion.X,before,motion.Y,out landed))AttachWindow(landed);
            }
            if(!motion.IsPerched)throw new Exception("A falling pet did not land on the window.");
            DesktopFeatureTests.RenderPreview(host,atlas.GetFrame(4,motion.Size,false),windowSupport,folder);
            ReturnToDesktop();
            if(motion.IsPerched||!returningToDesktop||motion.State!=PetState.Fall)throw new Exception("Return to desktop did not release the window.");
            SitOnWindow();
            if(!motion.IsPerched||returningToDesktop)throw new Exception("Explicit perching did not end the return-to-desktop action.");
            host.WindowState=FormWindowState.Minimized;Application.DoEvents();RefreshPerch();
            if(motion.IsPerched||motion.State!=PetState.Fall)throw new Exception("Minimized window left pet floating.");
            host.WindowState=FormWindowState.Normal;Application.DoEvents();SitOnWindow();
            if(!motion.IsPerched)throw new Exception("Restored window could not be used again.");
            host.Close();Application.DoEvents();RefreshPerch();
            if(motion.IsPerched)throw new Exception("Closed window left pet attached.");
            ShowChatPanel();
            chatPanel.SavePreview(Path.Combine(folder,"chat-preview.png"));
            chatPanel.Close();
            return "PASS: integrated window sit/follow/resize; thinking and reading while perched; care has priority; falling keeps its pose; automatic landing; detach on minimize/close; chat opens and remains responsive.";
        }
        public void ConfigureSmokeTest(string folder)
        {
            smokeFolder=folder;Directory.CreateDirectory(folder);smokeTime=0;
        }
        private void RunSmokeStep(double dt)
        {
            smokeTime+=dt;int step=(int)(smokeTime/0.6);
            if(step==smokeStep)return;smokeStep=step;
            switch(step)
            {
                case 0: motion.Command(PetState.Walk);break;
                case 1: motion.Command(PetState.Sit);break;
                case 2: motion.Command(PetState.Sleep);break;
                case 3:
                    BeginDragging(new Point((int)motion.X+90,(int)motion.Y+60));
                    MoveDragging(new Point(currentScreen.WorkingArea.Left+350,currentScreen.WorkingArea.Top+220));
                    if(motion.State!=PetState.Drag)throw new Exception("Drag did not start.");
                    EndDragging();break;
                case 4: motion.SetSize(240);break;
                case 5: motion.SetSize(120);break;
                case 6: pauseItem.PerformClick();if(!motion.Paused)throw new Exception("Pause menu failed.");break;
                case 7: pauseItem.PerformClick();topItem.PerformClick();if(TopMost)throw new Exception("Always-on-top toggle failed.");break;
                case 8: topItem.PerformClick();RecallPet();break;
                case 9: motion.Command(PetState.Shrug);break;
                case 10:
                    RecallPet();motion.SetSize(180);
                    int beforePats=care.TotalPats;
                    Point headPoint=new Point((int)motion.X+90,(int)motion.Y+45);
                    PointerPressed(headPoint,1);PointerMoved(new Point(headPoint.X+1,headPoint.Y+1));PointerReleased();
                    if(care.TotalPats!=beforePats+1||motion.State==PetState.Drag)throw new Exception("Head click did not pat cleanly.");
                    if(reaction!=CareReaction.Pat)throw new Exception("Pat animation did not start.");
                    PointerPressed(headPoint,2);PointerReleased();
                    if(reaction!=CareReaction.Pat||motion.State==PetState.Shrug)throw new Exception("Rapid head pats triggered a shrug.");
                    break;
                case 11: ShowCarePanel();break;
                case 12:
                    int beforeMeals=care.TotalMeals;
                    foodMenu.DropDownItems[(int)FoodKind.Cookie].PerformClick();
                    if(care.TotalMeals!=beforeMeals+1||reaction!=CareReaction.Eat)throw new Exception("Feeding menu or eating animation failed.");
                    break;
                case 13:
                    int afterMeal=care.TotalMeals;
                    if(FeedPet(FoodKind.Berry).Accepted||care.TotalMeals!=afterMeal)throw new Exception("Food cooldown did not reject repeated feeding.");
                    break;
                case 14:
                    carePanel.SavePreview(Path.Combine(smokeFolder,"care-panel-live.png"));
                    if(PetCare.Load(carePath).TotalMeals!=care.TotalMeals)throw new Exception("Care progress was not saved.");
                    using(Bitmap eating=new Bitmap(effects.Frame(atlas,240,CareReaction.Eat,0.5,FoodKind.Cookie)))eating.Save(Path.Combine(smokeFolder,"eating-reaction.png"),System.Drawing.Imaging.ImageFormat.Png);
                    break;
                case 15:
                    int beforeDrag=care.TotalPats;
                    Point press=new Point((int)motion.X+90,(int)motion.Y+45);
                    PointerPressed(press,1);PointerMoved(new Point(press.X+55,press.Y-90));
                    if(motion.State!=PetState.Drag)throw new Exception("Deliberate movement did not begin dragging.");
                    PointerReleased();
                    if(care.TotalPats!=beforeDrag)throw new Exception("Dragging incorrectly counted as a pat.");
                    break;
                case 16:
                    if(carePanel!=null)carePanel.Close();
                    RecallPet();PointerPressed(new Point((int)motion.X+90,(int)motion.Y+155),2);PointerReleased();
                    if(motion.State!=PetState.Shrug)throw new Exception("Body double-click did not shrug.");
                    break;
                case 17:
                    File.WriteAllText(Path.Combine(smokeFolder,"ui-smoke-results.txt"),"PASS: desktop app, movement and existing controls; head-click versus drag threshold; pat reaction; care panel; food menu and eating reaction; cooldown; saved meals; clean panel and app exit.\r\n");
                    Close();break;
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing=true;timer.Stop();care.Save(carePath);if(carePanel!=null&&!carePanel.IsDisposed)carePanel.Close();if(chatPanel!=null&&!chatPanel.IsDisposed)chatPanel.Close();base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!cleanedUp)
            {
                cleanedUp=true;
                closing=true;SystemEvents.DisplaySettingsChanged-=DisplayChanged;
                if(feedback!=null)feedback.Dispose();if(carePanel!=null&&!carePanel.IsDisposed)carePanel.Dispose();effects.Dispose();
                if(chatPanel!=null&&!chatPanel.IsDisposed)chatPanel.Dispose();localAi.Dispose();
                timer.Stop();timer.Dispose();tray.Visible=false;tray.Dispose();menu.Dispose();petIcon.Dispose();
            }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr handle);
    }
}
