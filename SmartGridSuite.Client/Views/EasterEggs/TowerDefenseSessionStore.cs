using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Fictional arcade-only run snapshot. Written separately from permanent
    /// research. Window close suspends a run rather than ending/banking it.
    /// </summary>
    internal sealed class TowerDefenseSession
    {
        public TowerDefenseSession() { }
        public int Version { get; set; } = 1;
        public int Wave { get; set; }
        public int Cash { get; set; }
        public int Score { get; set; }
        public int ThievesStopped { get; set; }
        public int InterferenceRemoved { get; set; }
        public double Health { get; set; }
        public double FenceHealth { get; set; }
        public bool Started { get; set; }
        public bool WaveActive { get; set; }
        public bool PerkPending { get; set; }
        public bool AutoNextPurchased { get; set; }
        public bool AutoNextEnabled { get; set; }
        public double AutoNextRemaining { get; set; } = -1;
        public int ThievesToSpawn { get; set; }
        public int JammersToSpawn { get; set; }
        public int LightningToSpawn { get; set; }
        public double SpawnThiefIn { get; set; }
        public double SpawnJammerIn { get; set; }
        public double SpawnLightningIn { get; set; }
        public double WarningRemaining { get; set; } = -1;
        public double SecurityCooldown { get; set; }
        public double RfCooldown { get; set; }
        public double PatrolCooldown { get; set; }
        public double DroneCooldown { get; set; }
        public double LeftGunCooldown { get; set; }
        public double RightGunCooldown { get; set; }
        public double ElapsedSeconds { get; set; }
        public long PendingResearch { get; set; }
        public int[] RunLevels { get; set; } = Array.Empty<int>();
        public int Category { get; set; }
        public int Row { get; set; }
        public List<TowerDefenseThreatSnapshot> Enemies { get; set; } = new();
        public List<TowerDefenseRfSnapshot> InterferenceTowers { get; set; } = new();
    }

    internal sealed class TowerDefenseThreatSnapshot
    {
        public TowerDefenseThreatSnapshot() { }
        public int Kind { get; set; }
        public bool FenceHitDone { get; set; }
        public double FenceVaultDuration { get; set; }
        public double FenceVaultRemaining { get; set; }
        public double ClimbSeconds { get; set; }
        public double Angle { get; set; }
        public double Distance { get; set; }
        public double Speed { get; set; }
        public double Health { get; set; }
        public double MaxHealth { get; set; }
    }

    internal sealed class TowerDefenseRfSnapshot
    {
        public TowerDefenseRfSnapshot() { }
        public double X { get; set; }
        public double Y { get; set; }
        public double Age { get; set; }
        public double Health { get; set; }
        public double MaxHealth { get; set; }
    }

    internal static class TowerDefenseSessionStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartGridSuite", "Arcade", "tower-defense-suspended-v1.json");

        public static TowerDefenseSession? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var session = JsonSerializer.Deserialize<TowerDefenseSession>(
                    File.ReadAllText(FilePath));
                if (session == null || session.Version != 1 ||
                    session.Wave < 0 || session.Wave > 100000 ||
                    session.Cash < 0 || session.Score < 0 ||
                    session.Health < 0 || session.RunLevels == null ||
                    session.RunLevels.Length != TowerDefenseProgression.Catalog.Length ||
                    session.Enemies == null || session.InterferenceTowers == null ||
                    session.Enemies.Count > 500 || session.InterferenceTowers.Count > 100 ||
                    !double.IsFinite(session.Health) ||
                    !double.IsFinite(session.FenceHealth) ||
                    !double.IsFinite(session.ElapsedSeconds))
                    return null;
                return session;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or JsonException or ArgumentException)
            {
                Debug.WriteLine("Arcade suspended run could not be loaded: " + ex);
                return null;
            }
        }

        public static void Save(TowerDefenseSession session)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(session));
                File.Move(temp, FilePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException)
            {
                Debug.WriteLine("Arcade suspended run could not be saved: " + ex);
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine("Arcade suspended-run cleanup failed: " + ex);
            }
        }
    }
}
