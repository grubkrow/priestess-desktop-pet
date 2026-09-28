using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace PriestessPet
{
    public enum FoodKind { Berry, Cookie, Onigiri }

    public sealed class CareResult
    {
        public bool Accepted { get; private set; }
        public string Message { get; private set; }

        public CareResult(bool accepted, string message)
        {
            Accepted = accepted;
            Message = message;
        }
    }

    // Care advances only from the caller's active-time updates. Loading a save
    // never subtracts food or affection for time spent away from the app.
    public sealed class PetCare
    {
        private double patCooldown;
        private double mealCooldown;

        public double Fullness { get; private set; }
        public double Affection { get; private set; }
        public int TotalPats { get; private set; }
        public int TotalMeals { get; private set; }

        public string Mood
        {
            get
            {
                if (Fullness < 25) return "Peckish";
                if (Affection >= 75) return "Adoring";
                if (Fullness >= 85) return "Cozy and full";
                if (Affection >= 40) return "Happy";
                return "Content";
            }
        }

        public PetCare()
        {
            Fullness = 70;
            Affection = 20;
        }

        public void Update(double realSeconds)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds <= 0) return;
            patCooldown = Math.Max(0, patCooldown - realSeconds);
            mealCooldown = Math.Max(0, mealCooldown - realSeconds);
            Fullness = Clamp(Fullness - realSeconds * (8.0 / 3600.0));
            Affection = Clamp(Affection - realSeconds * (2.0 / 3600.0));
        }

        public CareResult Pat()
        {
            if (patCooldown > 0.000000001)
                return new CareResult(false, "One gentle pat at a time!");
            patCooldown = 0.7;
            Affection = Clamp(Affection + 4);
            if (TotalPats < int.MaxValue) TotalPats++;
            return new CareResult(true, "Hehe... more headpats?");
        }

        public CareResult Feed(FoodKind food)
        {
            double foodValue;
            double affectionValue;
            string message;
            switch (food)
            {
                case FoodKind.Berry:
                    foodValue = 10; affectionValue = 3; message = "A sweet little berry. Thank you!"; break;
                case FoodKind.Cookie:
                    foodValue = 16; affectionValue = 5; message = "A cookie for me? Hehe!"; break;
                case FoodKind.Onigiri:
                    foodValue = 28; affectionValue = 4; message = "Mmm... a lovely rice ball!"; break;
                default:
                    return new CareResult(false, "Let's pick something from the food menu.");
            }
            if (Fullness >= 95)
                return new CareResult(false, "I'm full for now. Save me a snack for later!");
            if (mealCooldown > 0.000000001)
                return new CareResult(false, "Still enjoying my last bite!");
            mealCooldown = 3;
            Fullness = Clamp(Fullness + foodValue);
            Affection = Clamp(Affection + affectionValue);
            if (TotalMeals < int.MaxValue) TotalMeals++;
            return new CareResult(true, message);
        }

        public static string FoodName(FoodKind food)
        {
            switch (food)
            {
                case FoodKind.Berry: return "Berry";
                case FoodKind.Cookie: return "Cookie";
                case FoodKind.Onigiri: return "Onigiri";
                default: return "Snack";
            }
        }

        public static string FoodDescription(FoodKind food)
        {
            switch (food)
            {
                case FoodKind.Berry: return "Light snack: +10 fullness, +3 affection";
                case FoodKind.Cookie: return "Sweet treat: +16 fullness, +5 affection";
                case FoodKind.Onigiri: return "Rice ball: +28 fullness, +4 affection";
                default: return "Choose a snack for Priestess";
            }
        }

        public static PetCare Load(string path)
        {
            PetCare result = new PetCare();
            if (string.IsNullOrWhiteSpace(path)) return result;
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Prohibit;
                settings.XmlResolver = null;
                settings.MaxCharactersInDocument = 16384;
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(path, settings)) document.Load(reader);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "priestess-care" || root.NamespaceURI.Length != 0 ||
                    root.GetAttribute("version") != "1") return result;

                double fullness;
                double affection;
                long pats;
                long meals;
                if (!TryNumber(root, "fullness", out fullness) || !TryNumber(root, "affection", out affection) ||
                    !TryCounter(root, "total-pats", out pats) || !TryCounter(root, "total-meals", out meals)) return result;
                result.Fullness = Clamp(fullness);
                result.Affection = Clamp(affection);
                result.TotalPats = (int)Math.Max(0L, Math.Min(int.MaxValue, pats));
                result.TotalMeals = (int)Math.Max(0L, Math.Min(int.MaxValue, meals));
            }
            catch (Exception)
            {
                // Missing, malformed, inaccessible, or incompatible saves use defaults.
            }
            return result;
        }

        public bool Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string temporaryPath = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory) || Directory.Exists(fullPath)) return false;
                temporaryPath = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = new UTF8Encoding(false);
                settings.Indent = true;
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, settings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("priestess-care");
                        writer.WriteAttributeString("version", "1");
                        writer.WriteElementString("fullness", Fullness.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteElementString("affection", Affection.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteElementString("total-pats", TotalPats.ToString(CultureInfo.InvariantCulture));
                        writer.WriteElementString("total-meals", TotalMeals.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }
                }
                // Never delete a previous save to make room. A failed replacement
                // leaves it intact, including on read-only media or a locked file.
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
                return true;
            }
            catch (Exception) { return false; }
            finally
            {
                if (temporaryPath != null)
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (Exception) { }
                }
            }
        }

        private static double Clamp(double value) { return Math.Max(0, Math.Min(100, value)); }

        private static bool TryNumber(XmlElement root, string name, out double value)
        {
            value = 0;
            XmlNodeList nodes = root.SelectNodes(name);
            return nodes.Count == 1 && double.TryParse(nodes[0].InnerText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryCounter(XmlElement root, string name, out long value)
        {
            value = 0;
            XmlNodeList nodes = root.SelectNodes(name);
            return nodes.Count == 1 && long.TryParse(nodes[0].InnerText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
