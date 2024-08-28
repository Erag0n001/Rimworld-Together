﻿using Shared;
using static Shared.CommonEnumerators;

namespace GameServer
{
    public static class SiteManager
    {
        //Variables

        public readonly static string fileExtension = ".mpsite";

        public static void ParseSitePacket(ServerClient client, Packet packet)
        {
            if (!Master.actionValues.EnableSites)
            {
                ResponseShortcutManager.SendIllegalPacket(client, "Tried to use disabled feature!");
                return;
            }

            SiteData siteData = Serializer.ConvertBytesToObject<SiteData>(packet.contents);

            switch(siteData.siteStepMode)
            {
                case SiteStepMode.Build:
                    AddNewSite(client, siteData);
                    break;

                case SiteStepMode.Destroy:
                    DestroySite(client, siteData);
                    break;

                case SiteStepMode.Info:
                    GetSiteInfo(client, siteData);
                    break;

                case SiteStepMode.Deposit:
                    DepositWorkerIntoSite(client, siteData);
                    break;

                case SiteStepMode.Retrieve:
                    RetrieveWorkerFromSite(client, siteData);
                    break;
            }
        }

        public static bool CheckIfTileIsInUse(int tileToCheck)
        {
            string[] sites = Directory.GetFiles(Master.sitesPath);
            foreach (string site in sites)
            {
                if (!site.EndsWith(fileExtension)) continue;

                SiteFile siteFile = Serializer.SerializeFromFile<SiteFile>(site);
                if (siteFile.Tile == tileToCheck) return true;
            }

            return false;
        }

        public static void ConfirmNewSite(ServerClient client, SiteFile siteFile)
        {
            SaveSite(siteFile);

            SiteData siteData = new SiteData();
            siteData.siteStepMode = SiteStepMode.Build;
            siteData.siteFile = siteFile;

            foreach (ServerClient cClient in NetworkHelper.GetConnectedClientsSafe())
            {
                siteData.goodwill = GoodwillManager.GetSiteGoodwill(cClient, siteFile);
                Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);

                cClient.listener.EnqueuePacket(packet);
            }

            siteData.siteStepMode = SiteStepMode.Accept;
            Packet rPacket = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);
            client.listener.EnqueuePacket(rPacket);

            Logger.Warning($"[Created site] > {client.userFile.Username}");
        }

        public static void SaveSite(SiteFile siteFile)
        {
            Serializer.SerializeToFile(Path.Combine(Master.sitesPath, siteFile.Tile + fileExtension), siteFile);
        }

        public static SiteFile[] GetAllSites()
        {
            List<SiteFile> sitesList = new List<SiteFile>();

            string[] sites = Directory.GetFiles(Master.sitesPath);
            foreach (string site in sites)
            {
                if (!site.EndsWith(fileExtension)) continue;
                sitesList.Add(Serializer.SerializeFromFile<SiteFile>(site));
            }

            return sitesList.ToArray();
        }

        public static SiteFile[] GetAllSitesFromUsername(string username)
        {
            List<SiteFile> sitesList = new List<SiteFile>();

            string[] sites = Directory.GetFiles(Master.sitesPath);
            foreach (string site in sites)
            {
                if (!site.EndsWith(fileExtension)) continue;

                SiteFile siteFile = Serializer.SerializeFromFile<SiteFile>(site);
                if (siteFile.FactionFile == null && siteFile.Owner == username) sitesList.Add(siteFile);
            }

            return sitesList.ToArray();
        }

        public static SiteFile GetSiteFileFromTile(int tileToGet)
        {
            string[] sites = Directory.GetFiles(Master.sitesPath);
            foreach (string site in sites)
            {
                if (!site.EndsWith(fileExtension)) continue;

                SiteFile siteFile = Serializer.SerializeFromFile<SiteFile>(site);
                if (siteFile.Tile == tileToGet) return siteFile;
            }

            return null;
        }

        private static void AddNewSite(ServerClient client, SiteData siteData)
        {
            if (SettlementManager.CheckIfTileIsInUse(siteData.siteFile.Tile)) ResponseShortcutManager.SendIllegalPacket(client, $"A site tried to be added to tile {siteData.siteFile.Tile}, but that tile already has a settlement");
            else if (CheckIfTileIsInUse(siteData.siteFile.Tile)) ResponseShortcutManager.SendIllegalPacket(client, $"A site tried to be added to tile {siteData.siteFile.Tile}, but that tile already has a site");
            else
            {
                SiteFile siteFile = null;

                if (siteData.siteFile.FactionFile != null)
                {
                    FactionFile factionFile = client.userFile.FactionFile;

                    if (FactionManagerHelper.GetMemberRank(factionFile, client.userFile.Username) == FactionRanks.Member)
                    {
                        ResponseShortcutManager.SendNoPowerPacket(client, new PlayerFactionData());
                        return;
                    }

                    else
                    {
                        siteFile = new SiteFile();
                        siteFile.Tile = siteData.siteFile.Tile;
                        siteFile.Owner = client.userFile.Username;
                        siteFile.Type = siteData.siteFile.Type;
                        siteFile.FactionFile = factionFile;
                    }
                }

                else
                {
                    siteFile = new SiteFile();
                    siteFile.Tile = siteData.siteFile.Tile;
                    siteFile.Owner = client.userFile.Username;
                    siteFile.Type = siteData.siteFile.Type;
                }

                ConfirmNewSite(client, siteFile);
            }
        }

        private static void DestroySite(ServerClient client, SiteData siteData)
        {
            SiteFile siteFile = GetSiteFileFromTile(siteData.siteFile.Tile);

            if (siteFile.FactionFile != null)
            {
                if (siteFile.FactionFile.name != client.userFile.FactionFile.name)
                {
                    ResponseShortcutManager.SendIllegalPacket(client, $"The site at tile {siteData.siteFile.Tile} was attempted to be destroyed by {client.userFile.Username}, but player wasn't a part of faction {siteFile.FactionFile.name}");
                }

                else
                {
                    FactionFile factionFile = client.userFile.FactionFile;
                    if (FactionManagerHelper.GetMemberRank(factionFile, client.userFile.Username) != FactionRanks.Member) DestroySiteFromFile(siteFile);
                    else ResponseShortcutManager.SendNoPowerPacket(client, new PlayerFactionData());
                }
            }

            else
            {
                if (siteFile.Owner != client.userFile.Username) ResponseShortcutManager.SendIllegalPacket(client, $"The site at tile {siteData.siteFile.Tile} was attempted to be destroyed by {client.userFile.Username}, but the player {siteFile.Owner} owns it");
                else if (siteFile.WorkerData != null) ResponseShortcutManager.SendWorkerInsidePacket(client);
                else DestroySiteFromFile(siteFile);
            }
        }

        public static void DestroySiteFromFile(SiteFile siteFile)
        {
            SiteData siteData = new SiteData();
            siteData.siteStepMode = SiteStepMode.Destroy;
            siteData.siteFile = siteFile;

            Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);
            NetworkHelper.SendPacketToAllClients(packet);

            File.Delete(Path.Combine(Master.sitesPath, siteFile.Tile + fileExtension));
            Logger.Warning($"[Remove site] > {siteFile.Tile}");
        }

        private static void GetSiteInfo(ServerClient client, SiteData siteData)
        {
            SiteFile siteFile = GetSiteFileFromTile(siteData.siteFile.Tile);
            siteData.siteFile = siteFile;

            Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);
            client.listener.EnqueuePacket(packet);
        }

        private static void DepositWorkerIntoSite(ServerClient client, SiteData siteData)
        {
            SiteFile siteFile = GetSiteFileFromTile(siteData.siteFile.Tile);

            if (siteFile.FactionFile != null)
            {
                ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} tried to deposit worker into faction site");
            }

            else
            {
                if (siteFile.Owner != client.userFile.Username)
                {
                    ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} tried to deposit a worker in the site at tile {siteData.siteFile.Tile}, but the player {siteFile.Owner} owns it");
                }

                else if (siteFile.WorkerData != null)
                {
                    ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} tried to deposit a worker in the site at tile {siteData.siteFile.Tile}, but the site already has a worker");
                }

                else
                {
                    siteFile.WorkerData = siteData.siteFile.WorkerData;
                    SaveSite(siteFile);
                }
            }
        }

        private static void RetrieveWorkerFromSite(ServerClient client, SiteData siteData)
        {
            SiteFile siteFile = GetSiteFileFromTile(siteData.siteFile.Tile);

            if (siteFile.FactionFile != null)
            {
                ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} tried to extract worker from faction site");
            }

            else
            {
                if (siteFile.Owner != client.userFile.Username)
                {
                    ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} attempted to retrieve a worker from the site at tile {siteData.siteFile.Tile}, but the player {siteFile.Owner} of faction {siteFile.FactionFile.name} owns it");
                }

                else if (siteFile.WorkerData == null)
                {
                    ResponseShortcutManager.SendIllegalPacket(client, $"Player {client.userFile.Username} attempted to retrieve a worker from the site at tile {siteData.siteFile.Tile}, but it has no workers");
                }

                else
                {
                    siteData.siteFile.WorkerData = siteFile.WorkerData;
                    siteFile.WorkerData = null;
                    SaveSite(siteFile);

                    Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);
                    client.listener.EnqueuePacket(packet);
                }
            }
        }

        public static void StartSiteTicker()
        {
            while (true)
            {
                Thread.Sleep(1800000);

                try { SiteRewardTick(); }
                catch (Exception e) { Logger.Error($"Site tick failed, this should never happen. Exception > {e}"); }
            }
        }

        public static void SiteRewardTick()
        {
            SiteFile[] sites = GetAllSites();

            SiteData siteData = new SiteData();
            siteData.siteStepMode = SiteStepMode.Reward;

            foreach (ServerClient client in NetworkHelper.GetConnectedClientsSafe())
            {
                siteData.sitesWithRewards.Clear();

                //Get player specific sites

                List<SiteFile> playerSites = sites.ToList().FindAll(fetch => fetch.FactionFile == null && fetch.Owner == client.userFile.Username);
                foreach (SiteFile site in playerSites)
                {
                    if (site.WorkerData != null)
                    {
                        siteData.sitesWithRewards.Add(site.Tile);
                    }
                }

                //Get faction specific sites

                if (client.userFile.FactionFile != null)
                {
                    List<SiteFile> factionSites = sites.ToList().FindAll(fetch => fetch.FactionFile != null && fetch.FactionFile.name == client.userFile.FactionFile.name);
                    foreach (SiteFile site in factionSites)
                    {
                        if (site.FactionFile != null) siteData.sitesWithRewards.Add(site.Tile);
                    }
                }

                if (siteData.sitesWithRewards.Count() > 0)
                {
                    Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.SitePacket), siteData);
                    client.listener.EnqueuePacket(packet);
                }
            }

            Logger.Message($"[Site tick]");
        }
    }
}
