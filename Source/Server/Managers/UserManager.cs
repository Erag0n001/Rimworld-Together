﻿using Shared;
using static Shared.CommonEnumerators;

namespace GameServer
{
    public static class UserManager
    {
        public static void LoginUser(ServerClient client, Packet packet)
        {
            LoginData loginData = Serializer.ConvertBytesToObject<LoginData>(packet.contents);

            if (!UserManagerHelper.CheckIfUserUpdated(client, loginData)) return;

            if (!UserManagerHelper.CheckLoginData(client, loginData, LoginMode.Login)) return;

            if (!UserManagerHelper.CheckIfUserExists(client, loginData, LoginMode.Login)) return;

            if (!UserManagerHelper.CheckIfUserAuthCorrect(client, loginData)) return;

            client.userFile.SetLoginDetails(loginData);

            client.LoadFromUserFile();

            Logger.Message($"[Handshake] > {client.userFile.SavedIP} | {client.userFile.Username}");

            if (UserManagerHelper.CheckIfUserBanned(client)) return;

            if (!UserManagerHelper.CheckWhitelist(client)) return;

            if (ModManager.CheckIfModConflict(client, loginData)) return;

            RemoveOldClientIfAny(client);

            PostLogin(client);
        }

        public static void RegisterUser(ServerClient client, Packet packet)
        {
            LoginData loginData = Serializer.ConvertBytesToObject<LoginData>(packet.contents);

            if (!UserManagerHelper.CheckIfUserUpdated(client, loginData)) return;

            if (!UserManagerHelper.CheckLoginData(client, loginData, LoginMode.Register)) return;

            if (UserManagerHelper.CheckIfUserExists(client, loginData, LoginMode.Register)) return;

            try
            {
                client.userFile.SetLoginDetails(loginData);

                client.userFile.SaveUserFile();

                LoginUser(client, packet);

                Logger.Message($"[Registered] > {client.userFile.Username}");
            }
            catch { SendLoginResponse(client, LoginResponse.RegisterError); }
        }

        private static void PostLogin(ServerClient client)
        {
            SendPlayerRecount();

            ServerGlobalDataManager.SendServerGlobalData(client);

            foreach(string str in ChatManager.defaultJoinMessages) ChatManager.SendSystemMessage(client, str);

            if (WorldManager.CheckIfWorldExists())
            {
                if (SaveManager.CheckIfUserHasSave(client)) SaveManager.SendSavePartToClient(client);
                else WorldManager.SendWorldFile(client);
            }
            else WorldManager.RequireWorldFile(client);
        }

        private static void RemoveOldClientIfAny(ServerClient client)
        {
            foreach (ServerClient cClient in Network.connectedClients.ToArray())
            {
                if (cClient == client) continue;
                else
                {
                    if (cClient.userFile.Username == client.userFile.Username)
                    {
                        SendLoginResponse(cClient, LoginResponse.ExtraLogin);
                    }
                }
            }
        }

        public static void SendPlayerRecount()
        {
            PlayerRecountData playerRecountData = new PlayerRecountData();
            playerRecountData.currentPlayers = Network.connectedClients.ToArray().Count().ToString();
            foreach(ServerClient client in Network.connectedClients.ToArray()) playerRecountData.currentPlayerNames.Add(client.userFile.Username);

            Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.PlayerRecountPacket), playerRecountData);
            foreach (ServerClient client in Network.connectedClients.ToArray()) client.listener.EnqueuePacket(packet);
        }

        public static void SendLoginResponse(ServerClient client, LoginResponse response, object extraDetails = null)
        {
            LoginData loginData = new LoginData();
            loginData.tryResponse = response;

            if (response == LoginResponse.WrongMods) loginData.extraDetails = (List<string>)extraDetails;
            else if (response == LoginResponse.WrongVersion) loginData.extraDetails = new List<string>() { CommonValues.executableVersion };

            Packet packet = Packet.CreatePacketFromObject(nameof(PacketHandler.LoginResponsePacket), loginData);
            client.listener.EnqueuePacket(packet);
            client.listener.disconnectFlag = true;
        }
    }

    public static class UserManagerHelper
    {
        //Variables

        public readonly static string fileExtension = ".mpuser";

        public static UserFile GetUserFile(ServerClient client)
        {
            string[] userFiles = Directory.GetFiles(Master.usersPath);

            foreach(string userFile in userFiles)
            {
                if (!userFile.EndsWith(fileExtension)) continue;

                UserFile file = Serializer.SerializeFromFile<UserFile>(userFile);
                if (file.Username == client.userFile.Username) return file;
            }

            return null;
        }

        public static UserFile GetUserFileFromName(string username)
        {
            string[] userFiles = Directory.GetFiles(Master.usersPath);

            foreach (string userFile in userFiles)
            {
                if (!userFile.EndsWith(fileExtension)) continue;

                UserFile file = Serializer.SerializeFromFile<UserFile>(userFile);
                if (file.Username == username) return file;
            }

            return null;
        }

        public static UserFile[] GetAllUserFiles()
        {
            List<UserFile> userFiles = new List<UserFile>();

            string[] existingUsers = Directory.GetFiles(Master.usersPath);
            foreach (string user in existingUsers) 
            {
                if (!user.EndsWith(fileExtension)) continue;
                userFiles.Add(Serializer.SerializeFromFile<UserFile>(user)); 
            }
            return userFiles.ToArray();
        }

        public static bool CheckIfUserIsConnected(string username)
        {
            List<ServerClient> connectedClients = Network.connectedClients.ToList();

            ServerClient toGet = connectedClients.Find(x => x.userFile.Username == username);
            if (toGet != null) return true;
            else return false;
        }

        public static ServerClient GetConnectedClientFromUsername(string username)
        {
            List<ServerClient> connectedClients = Network.connectedClients.ToList();
            return connectedClients.Find(x => x.userFile.Username == username);
        }

        public static bool CheckIfUserExists(ServerClient client, LoginData data, LoginMode mode)
        {
            string[] existingUsers = Directory.GetFiles(Master.usersPath);

            foreach (string user in existingUsers)
            {
                if (!user.EndsWith(fileExtension)) continue;

                UserFile existingUser = Serializer.SerializeFromFile<UserFile>(user);
                if (existingUser.Username.ToLower() == data.username.ToLower())
                {
                    if (mode == LoginMode.Register) UserManager.SendLoginResponse(client, LoginResponse.RegisterInUse);
                    return true;
                }
            }

            if (mode == LoginMode.Login) UserManager.SendLoginResponse(client, LoginResponse.InvalidLogin);
            return false;
        }

        public static bool CheckIfUserAuthCorrect(ServerClient client, LoginData data)
        {
            string[] existingUsers = Directory.GetFiles(Master.usersPath);

            foreach (string user in existingUsers)
            {
                if (!user.EndsWith(fileExtension)) continue;
                UserFile existingUser = Serializer.SerializeFromFile<UserFile>(user);
                if (existingUser.Username == data.username)
                {
                    if (existingUser.Password == data.password) return true;
                    else break;
                }
            }

            UserManager.SendLoginResponse(client, LoginResponse.InvalidLogin);
            return false;
        }

        public static bool CheckIfUserBanned(ServerClient client)
        {
            if (!client.userFile.IsBanned) return false;
            else
            {
                UserManager.SendLoginResponse(client, LoginResponse.BannedLogin);
                return true;
            }
        }

        public static bool CheckLoginData(ServerClient client, LoginData data, LoginMode mode)
        {
            bool isInvalid = false;
            if (string.IsNullOrWhiteSpace(data.username)) isInvalid = true;
            if (string.IsNullOrWhiteSpace(data.password)) isInvalid = true;
            if (data.username.Any(Char.IsWhiteSpace)) isInvalid = true;
            if (data.username.Length > 32) isInvalid = true;
            if (data.password.Length > 64) isInvalid = true;

            if (!isInvalid) return true;
            else
            {
                if (mode == LoginMode.Login) UserManager.SendLoginResponse(client, LoginResponse.InvalidLogin);
                else if (mode == LoginMode.Register) UserManager.SendLoginResponse(client, LoginResponse.RegisterError);
                return false;
            }
        }

        public static bool CheckWhitelist(ServerClient client)
        {
            if (!Master.whitelist.UseWhitelist) return true;
            else
            {
                foreach (string str in Master.whitelist.WhitelistedUsers)
                {
                    if (str == client.userFile.Username) return true;
                }
            }

            UserManager.SendLoginResponse(client, LoginResponse.Whitelist);
            return false;
        }

        public static bool CheckIfUserUpdated(ServerClient client, LoginData loginData)
        {
            if (loginData.clientVersion == CommonValues.executableVersion) return true;
            else
            {
                Logger.Warning($"[Version Mismatch] > {client.userFile.Username}");
                UserManager.SendLoginResponse(client, LoginResponse.WrongVersion);
                return false;
            }
        }

        public static int[] GetUserStructuresTilesFromUsername(string username)
        {
            SettlementFile[] settlements = SettlementManager.GetAllSettlements().ToList().FindAll(x => x.owner == username).ToArray();
            SiteFile[] sites = SiteManager.GetAllSites().ToList().FindAll(x => x.owner == username).ToArray();

            List<int> tilesToExclude = new List<int>();
            foreach (SettlementFile settlement in settlements) tilesToExclude.Add(settlement.tile);
            foreach (SiteFile site in sites) tilesToExclude.Add(site.tile);

            return tilesToExclude.ToArray();
        }
    }
}
