using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    // Separate permanent (research credits) and per-run (cash) upgrade tiers.
    // All research is fictional, local-only, and unrelated to operational DBs.
    internal sealed class TowerDefenseProgression
    {
        internal sealed record Upgrade(
            string Name, string Detail, int Category, int BaseCash,
            int CashSlope, int BaseResearch, int UnlockWave);

        internal static readonly Upgrade[] Catalog =
        {
            // OPERATIONS: automated threat removal and tower countermeasures.
            new("Security Patrol", "More thieves intercepted per patrol", 0, 85, 55, 24, 0),
            new("Patrol Frequency", "Faster automatic security response", 0, 100, 65, 24, 0),
            new("Patrol Coverage", "Detect threats farther from the tower", 0, 110, 70, 26, 0),
            new("Response Vehicles", "Additional thieves removed per dispatch", 0, 190, 110, 50, 3),
            new("RF Response Team", "Remove more illegal RF towers per sweep", 0, 155, 95, 35, 2),
            new("Counter-RF Filters", "Shorten the lifetime of RF interference", 0, 140, 90, 38, 0),
            new("Drone Interceptor", "Automatic countermeasure beam", 0, 260, 180, 75, 5),
            new("Drone Range", "Expand the interceptor's effective radius", 0, 200, 140, 55, 5),

            // PROTECTION: survive lightning, trespass, and sustained interference.
            new("Tower Reinforcement", "Increase maximum tower integrity", 1, 125, 78, 30, 0),
            new("Field Maintenance", "Regenerate tower integrity every second", 1, 150, 98, 40, 2),
            new("Lightning Arrestors", "Reduce direct lightning strike damage", 1, 180, 110, 42, 0),
            new("Surge Suppression", "Reduce storm and surge damage", 1, 160, 98, 39, 0),
            new("Security Fence", "Automatically repel nearby thieves", 1, 220, 140, 48, 0),
            new("Fence Durability", "Give installed fencing more integrity", 1, 155, 95, 39, 2),
            new("Fence Auto-Repair", "Regenerate security fence integrity", 1, 210, 125, 55, 5),
            new("Grounding Grid", "Reduce all tower damage slightly", 1, 260, 175, 78, 8),

            // LOGISTICS: fund the radio shop's growing defensive network.
            new("Dispatch Contract", "Earn additional cash after every wave", 2, 95, 66, 26, 0),
            new("Scrap Recovery", "Earn more for stopping copper thieves", 2, 110, 70, 28, 0),
            new("Crew Training", "Shorten Q/E sweep cooldowns", 2, 140, 100, 41, 2),
            new("Research Funding", "Earn more permanent research credits", 2, 200, 125, 65, 4),
            new("Parts Discount", "Lower the cash cost of new upgrades", 2, 240, 145, 70, 4),
            new("Free Work Orders", "Chance of a free upgrade each wave", 2, 290, 175, 85, 7),
            new("Repair Supplies", "Restore more HP per paid tower repair", 2, 150, 95, 40, 0),
            new("Maintenance Dispatch", "Restore tower HP at wave completion", 2, 250, 160, 75, 9)
        };

        private sealed class SavedState
        {
            public int Version { get; set; } = 1;
            public long ResearchCredits { get; set; }
            public int BestWave { get; set; }
            public int BestScore { get; set; }
            public int[] Permanent { get; set; } = new int[Catalog.Length];
        }

        private readonly int[] _permanent = new int[Catalog.Length];
        private readonly int[] _run = new int[Catalog.Length];
        private bool _banked = true;

        public long Credits { get; private set; }
        public long PendingCredits { get; private set; }
        public int BestWave { get; private set; }
        public int BestScore { get; private set; }
        public int SelectedCategory { get; set; }
        public int SelectedRow { get; set; }
        public bool WorkshopOpen { get; set; }

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartGridSuite", "Arcade", "tower-defense-workshop-v1.json");

        public TowerDefenseProgression()
        {
            Load();
            NewRun();
        }

        public int Level(int index) => _run[index];
        public int PermanentLevel(int index) => _permanent[index];

        public bool Unlocked(int index, int currentWave) =>
            Math.Max(BestWave, currentWave) >= Catalog[index].UnlockWave;

        public int RunPrice(int index)
        {
            long l = _run[index];
            var def = Catalog[index];
            double cost = def.BaseCash + def.CashSlope * (double)l +
                26.0 * l * l;
            double discount = 1.0 / (1 + _run[20] * 0.025);
            if (index == 20) discount = 1.0; // prevent self-discount loops
            return (int)Math.Clamp(Math.Ceiling(cost * discount), 1, int.MaxValue);
        }

        public long WorkshopPrice(int index)
        {
            double l = _permanent[index];
            double cost = Catalog[index].BaseResearch * (1 + 0.56 * l + 0.16 * l * l);
            return Math.Max(1, (long)Math.Ceiling(Math.Min(cost, 1e15)));
        }

        public bool BuyRun(int index, ref long cash, int currentWave)
        {
            if (!Unlocked(index, currentWave))
                return false;
            var price = RunPrice(index);
            if (cash < price)
                return false;
            cash -= price;
            _run[index] = Math.Min(1000000, _run[index] + 1);
            return true;
        }

        public bool BuyWorkshop(int index)
        {
            if (!Unlocked(index, BestWave))
                return false;
            long price = WorkshopPrice(index);
            if (Credits < price)
                return false;
            Credits -= price;
            _permanent[index] = Math.Min(1000000, _permanent[index] + 1);
            _run[index] = Math.Max(_run[index], _permanent[index]);
            Save();
            return true;
        }

        public void FreeUpgrade(int index)
        {
            _run[index] = Math.Min(1000000, _run[index] + 1);
        }

        public void NewRun()
        {
            Array.Copy(_permanent, _run, Catalog.Length);
            PendingCredits = 0;
            _banked = false;
            WorkshopOpen = false;
            SelectedCategory = 0;
            SelectedRow = 0;
        }

        public void AddResearchCredits(long amount)
        {
            PendingCredits = Math.Min(1_000_000_000L,
                PendingCredits + Math.Max(0L, amount));
        }

        public void BankRun(int wave, int score)
        {
            if (_banked)
                return;
            _banked = true;
            BestWave = Math.Max(BestWave, wave);
            BestScore = Math.Max(BestScore, score);
            Credits = Math.Min(1_000_000_000_000_000L, Credits + PendingCredits);
            PendingCredits = 0;
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(FilePath));
                if (saved == null || saved.Version != 1)
                    return;

                Credits = Math.Max(0, saved.ResearchCredits);
                BestWave = Math.Max(0, saved.BestWave);
                BestScore = Math.Max(0, saved.BestScore);
                for (int i = 0; i < Catalog.Length && i < saved.Permanent.Length; i++)
                    _permanent[i] = Math.Clamp(saved.Permanent[i], 0, 1000000);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or JsonException or ArgumentException)
            {
                Debug.WriteLine("Arcade research state unavailable: " + ex);
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                var snapshot = new SavedState
                {
                    ResearchCredits = Credits,
                    BestWave = BestWave,
                    BestScore = BestScore,
                    Permanent = _permanent.ToArray()
                };
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(snapshot));
                File.Move(temp, FilePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException)
            {
                Debug.WriteLine("Arcade research could not save: " + ex);
            }
        }
    }
}
