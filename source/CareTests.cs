using System;
using System.Globalization;
using System.IO;

namespace PriestessPet
{
    public static class CareTests
    {
        public static string Run(string tempDirectory)
        {
            int assertions = 0;
            PetCare pet = new PetCare();
            Near(pet.Fullness, 70, "Default fullness", ref assertions);
            Near(pet.Affection, 20, "Default affection", ref assertions);
            Check(pet.TotalPats == 0 && pet.TotalMeals == 0 && pet.Mood == "Content", "Default counters and mood", ref assertions);
            Check(pet.Pat().Accepted, "First pat succeeds", ref assertions);
            Near(pet.Affection, 24, "Pat adds affection", ref assertions);
            Check(!pet.Pat().Accepted && pet.TotalPats == 1, "Pat cooldown rejects repeated clicks", ref assertions);
            pet.Update(0.69);
            Check(!pet.Pat().Accepted, "Pat remains on cooldown before boundary", ref assertions);
            pet.Update(0.01);
            Check(pet.Pat().Accepted && pet.TotalPats == 2, "Pat recovers exactly at boundary", ref assertions);

            FoodKind[] foods = { FoodKind.Berry, FoodKind.Cookie, FoodKind.Onigiri };
            double[] filling = { 10, 16, 28 };
            double[] loving = { 3, 5, 4 };
            for (int i = 0; i < foods.Length; i++)
            {
                pet = new PetCare();
                Check(pet.Feed(foods[i]).Accepted, "Food accepted: " + foods[i], ref assertions);
                Near(pet.Fullness, 70 + filling[i], "Food fullness: " + foods[i], ref assertions);
                Near(pet.Affection, 20 + loving[i], "Food affection: " + foods[i], ref assertions);
                Check(pet.TotalMeals == 1 && PetCare.FoodName(foods[i]).Length > 0 && PetCare.FoodDescription(foods[i]).Length > 0,
                    "Food counter and labels: " + foods[i], ref assertions);
            }
            pet = new PetCare();
            Check(!pet.Feed((FoodKind)999).Accepted && pet.TotalMeals == 0 && pet.Fullness == 70,
                "Unknown food causes no changes", ref assertions);
            pet.Feed(FoodKind.Berry);
            Check(!pet.Feed(FoodKind.Cookie).Accepted && pet.TotalMeals == 1, "Meal cooldown preserves counters", ref assertions);
            pet.Update(2.99);
            Check(!pet.Feed(FoodKind.Berry).Accepted, "Meal waits for full cooldown", ref assertions);
            pet.Update(0.01);
            Check(pet.Feed(FoodKind.Onigiri).Accepted && pet.Fullness == 100, "Meal resumes and fullness clamps", ref assertions);
            pet.Update(3);
            Check(!pet.Feed(FoodKind.Berry).Accepted && pet.TotalMeals == 2, "Full pet declines food", ref assertions);
            Check(pet.Pat().Accepted, "Fullness does not block affection", ref assertions);

            pet = new PetCare();
            pet.Update(3600);
            Near(pet.Fullness, 62, "Fullness declines eight points per active hour", ref assertions);
            Near(pet.Affection, 18, "Affection declines two points per active hour", ref assertions);
            pet.Pat();
            double beforeFullness = pet.Fullness;
            double beforeAffection = pet.Affection;
            pet.Update(double.NaN); pet.Update(double.PositiveInfinity); pet.Update(double.NegativeInfinity); pet.Update(-50); pet.Update(0);
            Check(pet.Fullness == beforeFullness && pet.Affection == beforeAffection && !pet.Pat().Accepted,
                "Invalid time does not corrupt values or cooldowns", ref assertions);
            pet.Update(double.MaxValue);
            Check(pet.Fullness == 0 && pet.Affection == 0 && pet.Pat().Accepted,
                "Huge valid elapsed time stays finite and clears cooldown", ref assertions);
            for (int i = 0; i < 50; i++) { pet.Update(0.7); pet.Pat(); }
            Near(pet.Affection, 100, "Affection caps at one hundred", ref assertions);
            Check(pet.Mood == "Peckish", "Low fullness appears as a gentle mood", ref assertions);

            string isolated = Path.Combine(Path.GetFullPath(tempDirectory), "priestess-care-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolated);
            string save = Path.Combine(isolated, "priestess-care.xml");
            string malformed = Path.Combine(isolated, "malformed.xml");
            string marker = Path.Combine(isolated, "unrelated.txt");
            string directoryTarget = Path.Combine(isolated, "a-directory");
            try
            {
                File.WriteAllText(marker, "Leave unrelated files alone.");
                pet = new PetCare(); pet.Pat(); pet.Feed(FoodKind.Cookie); pet.Update(900);
                Check(pet.Save(save), "First portable save succeeds", ref assertions);
                File.SetLastWriteTimeUtc(save, DateTime.UtcNow.AddYears(-1));
                PetCare loaded = PetCare.Load(save);
                Same(pet, loaded, "Round trip preserves care without offline decay", ref assertions);
                Check(loaded.Pat().Accepted, "Reload does not inherit old cooldowns", ref assertions);
                Check(loaded.Save(save), "Atomic save replacement succeeds", ref assertions);
                Same(loaded, PetCare.Load(save), "Replaced save round trips", ref assertions);

                string existing = File.ReadAllText(save);
                using (FileStream locked = new FileStream(save, FileMode.Open, FileAccess.Read, FileShare.None))
                    Check(!loaded.Save(save), "Locked target fails softly", ref assertions);
                Check(File.ReadAllText(save) == existing, "Failed write preserves previous save", ref assertions);
                Check(Directory.GetFiles(isolated, "*.tmp").Length == 0, "Failed writes leave no temporary files", ref assertions);

                File.WriteAllText(malformed, "<priestess-care><fullness>");
                Defaults(PetCare.Load(malformed), "Truncated XML defaults", ref assertions);
                File.WriteAllText(malformed, "<!DOCTYPE priestess-care [<!ENTITY external SYSTEM 'file:///does-not-exist'>]><priestess-care version='1'>&external;</priestess-care>");
                Defaults(PetCare.Load(malformed), "DTD and external entity documents are rejected", ref assertions);
                File.WriteAllText(malformed, Xml("NaN", "75", "5", "2"));
                Defaults(PetCare.Load(malformed), "NaN save defaults", ref assertions);
                File.WriteAllText(malformed, Xml("70", "Infinity", "5", "2"));
                Defaults(PetCare.Load(malformed), "Infinite save defaults", ref assertions);
                File.WriteAllText(malformed, Xml("70", "75", "5", "2").Replace("</priestess-care>", "<fullness>5</fullness></priestess-care>"));
                Defaults(PetCare.Load(malformed), "Ambiguous duplicate field defaults", ref assertions);
                File.WriteAllText(malformed, Xml("1000", "-50", "-99", "9223372036854775807"));
                loaded = PetCare.Load(malformed);
                Check(loaded.Fullness == 100 && loaded.Affection == 0 && loaded.TotalPats == 0 && loaded.TotalMeals == int.MaxValue,
                    "Saved numeric values and counters clamp", ref assertions);
                loaded.Update(3600); loaded.Feed(FoodKind.Berry);
                Check(loaded.TotalMeals == int.MaxValue, "Meal counter never overflows", ref assertions);
                File.WriteAllText(malformed, Xml("70", "100", "2147483647", "2"));
                loaded = PetCare.Load(malformed); loaded.Pat();
                Check(loaded.TotalPats == int.MaxValue && loaded.Affection == 100, "Pat counter and affection never overflow", ref assertions);

                CultureInfo priorCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                    loaded = new PetCare(); loaded.Update(1.23);
                    Check(loaded.Save(save), "Save works with comma-decimal locale", ref assertions);
                    Same(loaded, PetCare.Load(save), "Locale-independent XML values", ref assertions);
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture = priorCulture; }
                Defaults(PetCare.Load(Path.Combine(isolated, "missing.xml")), "Missing save defaults", ref assertions);
                Defaults(PetCare.Load(null), "Null save path defaults", ref assertions);
                Check(!pet.Save(null) && !pet.Save(Path.Combine(isolated, "missing-parent", "care.xml")),
                    "Invalid or unavailable save paths fail softly", ref assertions);
                Directory.CreateDirectory(directoryTarget);
                Check(!pet.Save(directoryTarget) && Directory.Exists(directoryTarget), "Save never replaces a directory", ref assertions);
                Check(File.ReadAllText(marker) == "Leave unrelated files alone.", "Care persistence leaves unrelated data unchanged", ref assertions);
            }
            finally
            {
                // Only remove the exact files/directories created by this test.
                if (File.Exists(save)) File.Delete(save);
                if (File.Exists(malformed)) File.Delete(malformed);
                if (File.Exists(marker)) File.Delete(marker);
                if (Directory.Exists(directoryTarget)) Directory.Delete(directoryTarget);
                if (Directory.Exists(isolated)) Directory.Delete(isolated);
            }
            return "PASS: " + assertions + " pet-care assertions (treats, pats, cooldowns, bounds, active-time decay, safe portable saves).";
        }

        private static string Xml(string fullness, string affection, string pats, string meals)
        {
            return "<priestess-care version='1'><fullness>" + fullness + "</fullness><affection>" + affection +
                "</affection><total-pats>" + pats + "</total-pats><total-meals>" + meals + "</total-meals></priestess-care>";
        }

        private static void Defaults(PetCare pet, string message, ref int assertions)
        {
            Check(pet.Fullness == 70 && pet.Affection == 20 && pet.TotalPats == 0 && pet.TotalMeals == 0, message, ref assertions);
        }

        private static void Same(PetCare expected, PetCare actual, string message, ref int assertions)
        {
            Check(Math.Abs(expected.Fullness - actual.Fullness) < 0.000000001 && Math.Abs(expected.Affection - actual.Affection) < 0.000000001 &&
                expected.TotalPats == actual.TotalPats && expected.TotalMeals == actual.TotalMeals, message, ref assertions);
        }

        private static void Near(double actual, double expected, string message, ref int assertions)
        {
            Check(!double.IsNaN(actual) && Math.Abs(actual - expected) < 0.000000001, message, ref assertions);
        }

        private static void Check(bool success, string message, ref int assertions)
        {
            assertions++;
            if (!success) throw new InvalidOperationException("Care test failed: " + message);
        }
    }
}
