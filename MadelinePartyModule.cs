﻿using Microsoft.Xna.Framework;
using Celeste;
using System;
using System.Collections.Generic;
using System.Linq;
using Celeste.Mod;
using MonoMod.RuntimeDetour;
using Monocle;
using Celeste.Mod.CelesteNet;
using MadelineParty.CelesteNet;
using Logger = Celeste.Mod.Logger;
using Celeste.Mod.CelesteNet.Client;
using Celeste.Mod.CelesteNet.DataTypes;

// TODO minigames for most bounces of oshiro, seekers, snowballs, etc...
// TODO survive the longest minigames

namespace MadelineParty {
    public class MadelinePartyModule : EverestModule {

        // Only one alive module instance can exist at any given time.
        public static MadelinePartyModule Instance;
        public static string START_ROOM = "Game_PlayerNumberSelect";
        public static string MAIN_ROOM = "Game_MainRoom";

        public Level level;

        public static bool celesteNetInstalled;
        private static bool celesteNetConnected {
            get {
                return CelesteNetClientModule.Instance?.Client?.Con != null;
            }
        }
        
        public MadelinePartyModule() {
            Instance = this;
        }

        // The secret code that goes in front of emotes to signify they're from this mod
        public const string emotePrefix = "g:mparty";

        // If you don't need to store any settings, => null
        public override Type SettingsType => null;

        // If you don't need to store any save data, => null
        public override Type SaveDataType => null;

        // Set up any hooks, event handlers and your mod in general here.
        // Load runs before Celeste itself has initialized properly.
        public override void Load() {
            // Stuff that runs orig(self) always
            /* ************************************************ */
            Everest.Events.Level.OnLoadEntity += Level_OnLoadEntity;
            Everest.Events.Level.OnLoadLevel += (level, playerIntro, isFromLoader) => {
                this.level = level;
            };
            DreamBlockRNGSyncer.Load();
            MinigameInfinityTrigger.Load();
            On.Celeste.LevelEnter.Go += (orig, session, fromSaveData) => {
                if (IsSIDMadelineParty(session.Area.GetSID()) && !session.Level.Equals(START_ROOM)) {
                    session.Level = START_ROOM;
                    session.RespawnPoint = session.GetSpawnPoint(Vector2.Zero);
                    //Player player = level.Tracker.GetEntity<Player>();
                    //sendToStart(player);
                }
                orig(session, fromSaveData);
            };
            On.Celeste.Player.Update += Player_Update;
            On.Celeste.Level.UnloadLevel += (orig, self) => {
                if (IsSIDMadelineParty(self.Session.Area.GetSID())) {
                    TextMenu menu = self.Entities.FindFirst<TextMenu>();
                    if (menu != null) {
                        self.PauseMainMenuOpen = false;
                        menu.RemoveSelf();
                        self.Paused = false;
                    }
                }
                orig(self);
            };
            MinigameSwitchGatherer.Load();

            // Stuff that doesn't always run orig(self)
            /* ******************************************* */
            using (new DetourContext("MadelineParty") {
                After = { "*" }
            }) {

            }
        }

        public static bool IsCelesteNetInstalled() {
            return celesteNetInstalled && celesteNetConnected;
        }

        public static bool IsSIDMadelineParty(string sid) {
            return sid.Equals("Brokemia/MadelineParty/madelineparty");
        }

        void Player_Update(On.Celeste.Player.orig_Update orig, Player self) {
            Level l = self.SceneAs<Level>();
            orig(self);

            if (l != null && IsSIDMadelineParty(l.Session.Area.GetSID())) {
                if (celesteNetInstalled) {
                    // If the player disconnects from a multiplayer game
                    if (GameData.playerNumber > 1 && !celesteNetConnected) {
                        Player player = level.Tracker.GetEntity<Player>();
                        sendToStart(player);
                    }
                }

                if (!l.Session.Level.Equals(START_ROOM) && (!l.Session.Level.Equals("Game_Lobby") || GameData.playerNumber == -1)) {
                    if (GameData.players.All((data) => data == null)) {
                        Player player = l.Tracker.GetEntity<Player>();
                        sendToStart(player);
                    } else {
                        foreach (uint id in GameData.celestenetIDs) {
                            if (playerInStartRoom(id)) {
                                Player player = l.Tracker.GetEntity<Player>();
                                sendToStart(player);
                            }
                        }
                    }
                }

                if(l.Entities.AmountOf<MinigameFinishTrigger>() == 0) {
                    l.CanRetry = false;
                }
            }

        }

        private bool playerInStartRoom(uint id) {
            if (CelesteNetClientModule.Instance?.Client?.Data != null && CelesteNetClientModule.Instance.Client.Data.TryGetRef(id, out DataPlayerState state)) {
                Console.WriteLine(state + " " + state.Level);
                return state?.Level?.Equals(START_ROOM) ?? true;
            }
            return false;
        }

        private void sendToStart(Player p) {
            level.OnEndOfFrame += delegate {
                Leader.StoreStrawberries(p.Leader);
                level.Remove(p);
                level.UnloadLevel();

                level.Session.Level = START_ROOM;
                level.Session.RespawnPoint = level.GetSpawnPoint(new Vector2(level.Bounds.Left, level.Bounds.Top));
                level.LoadLevel(Player.IntroTypes.None);

                Leader.RestoreStrawberries(level.Tracker.GetEntity<Player>().Leader);
            };
        }

        bool Level_OnLoadEntity(Celeste.Level l, Celeste.LevelData levelData, Vector2 offset, Celeste.EntityData entityData) {
            if (entityData.Name.StartsWith("madelineparty/", StringComparison.InvariantCulture)) {
                switch (entityData.Name.Substring("madelineparty/".Length)) {
                    case "leftbutton":
                        l.Add(new LeftButton(entityData, offset));
                        return true;
                    case "rightbutton":
                        l.Add(new RightButton(entityData, offset));
                        return true;
                    case "boardcontroller":
                        l.Add(new BoardController(entityData));
                        return true;
                    case "playernumberselect":
                        l.Add(new PlayerNumberSelect(entityData, offset));
                        return true;
                    case "playerselecttrigger":
                        l.Add(new PlayerSelectTrigger(entityData, offset));
                        return true;
                    case "gamescoreboard":
                        l.Add(new GameScoreboard(entityData, offset));
                        return true;
                    case "minigamefinishtrigger":
                        l.Add(new MinigameFinishTrigger(entityData, offset));
                        return true;
                    case "playerrankingcontroller":
                        l.Add(new PlayerRankingController());
                        return true;
                    case "gameendcontroller":
                        l.Add(new GameEndController());
                        return true;
                    case "theominigamecontroller":
                        l.Add(new MinigameTheoMover(entityData, offset));
                        return true;
                }
            }
            return false;
        }


        // Optional, initialize anything after Celeste has initialized itself properly.
        public override void Initialize() {
            // Set the shop prices
            GameData.itemPrices[GameData.Item.DOUBLEDICE] = 10;
        }

        // Optional, do anything requiring either the Celeste or mod content here.
        [Obsolete]
        public override void LoadContent() {
            celesteNetInstalled = Everest.Modules.Any(mod => mod.GetType().Name.Equals("CelesteNetClientModule"));
            BoardController.LoadContent();
        }

        // Unload the entirety of your mod's content, remove any event listeners and undo all hooks.
        public override void Unload() {
            Everest.Events.Level.OnLoadEntity -= Level_OnLoadEntity;
            On.Celeste.Player.Update -= Player_Update;
            MinigameSwitchGatherer.Unload();
        }

    }
}