﻿using System;
using System.Collections;
using System.Collections.Generic;
using Celeste;
using Celeste.Mod.CelesteNet.Client;
using Celeste.Mod.Entities;
using MadelineParty.CelesteNet;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

namespace MadelineParty {
    // Collect touch switches before your opponent gets them
    [CustomEntity("madelineparty/minigameSwitchGatherer")]
    [Tracked]
    public class MinigameSwitchGatherer : MinigameEntity {
        public static uint switchCount;
        public static List<Vector2> switchesOn = new List<Vector2>();
        public List<TouchSwitch> switches;
        public Coroutine endCoroutine;
        public MinigameScoreDisplay display;
        public Random rand = new Random();

        public MinigameSwitchGatherer(EntityData data, Vector2 offset) : base(data, offset) {
        }

        public static void Load() {
            On.Celeste.TouchSwitch.TurnOn += TouchSwitch_TurnOn;
        }

        public static void Unload() {
            On.Celeste.TouchSwitch.TurnOn -= TouchSwitch_TurnOn;
        }

        private static void TouchSwitch_TurnOn(On.Celeste.TouchSwitch.orig_TurnOn orig, TouchSwitch self) {
            if(MadelinePartyModule.IsSIDMadelineParty(self.SceneAs<Level>().Session.Area.SID)) {
                MinigameSwitchGatherer gatherer;
                if ((gatherer = self.Scene.Tracker.GetEntity<MinigameSwitchGatherer>()) != null) {
                    if(switchesOn.Contains(self.Position)) {
                        gatherer.CollectSwitch(self.Position);
                    } else {
                        throw new Exception("Hit switch that shouldn't be on");
                    }
                    return;
                }
            }

            orig(self);
        }

        public void CollectSwitch(Vector2 pos) {
            switchCount++;
            GameData.minigameStatus[GameData.realPlayerID] = switchCount;

            TouchSwitch ts = switches.Find((s) => s.Position == pos);
            DeactivateSwitch(ts);
            for (int i = 0; i < 32; i++) {
                float num = Calc.Random.NextFloat((float)Math.PI * 2f);
                level.Particles.Emit(TouchSwitch.P_FireWhite, ts.Position + Calc.AngleToVector(num, 6f), num);
            }
            if (MadelinePartyModule.IsCelesteNetInstalled()) {
                CelesteNetSendMinigameStatus(switchCount);
                CelesteNetSendVector2(pos, -1);
            }
            if (GameData.gnetHost && switchesOn.Count == 0) {
                NewSwitcheDistribution(ts);
            }
        }

        private void NewSwitcheDistribution(TouchSwitch last) {
            TouchSwitch newSwitch;
            // Make sure not to get the same switch twice in a row
            while ((newSwitch = switches[rand.Next(switches.Count)]) == last) ;

            ActivateSwitch(newSwitch);

            // After 10 seconds, have a 50% chance to spawn a second switch
            if (level.RawTimeActive - startTime >= 10 && rand.NextFloat() > 0.5f) {
                TouchSwitch newSwitch2;
                // Make sure not to get the same switch twice in a row and not to get two switches in the same spot
                while ((newSwitch2 = switches[rand.Next(switches.Count)]) == last || newSwitch2 == newSwitch) ;

                ActivateSwitch(newSwitch2);
            }
            if (MadelinePartyModule.IsCelesteNetInstalled()) {
                foreach (Vector2 switchPos in switchesOn) {
                    CelesteNetSendVector2(switchPos, 1);
                }
            }
        }

        public override void Added(Scene scene) {
            base.Added(scene);
        }

        public override void Awake(Scene scene) {
            base.Awake(scene);
            if(!started) {
                switchesOn.Clear();
            }
            switches = scene.Tracker.GetEntities<TouchSwitch>().ConvertAll((e) => (TouchSwitch) e);
            foreach (TouchSwitch ts in switches) {
                if (!switchesOn.Contains(ts.Position)) {
                    DeactivateSwitch(ts);
                }
            }
            //Add(new Coroutine(level.ZoomTo(new Vector2(level.Bounds.Width/2f, level.Bounds.Height/2f), 8*22.5f/level.Bounds.Height, 3f)));
        }

        protected override void AfterStart() {
            base.AfterStart();
            // Reset timer so it starts at 30 instead of (30 - the time it takes to count down)
            startTime = level.RawTimeActive;
            level.Add(display = new MinigameScoreDisplay(this));
            if(GameData.gnetHost) {
                TouchSwitch ts = switches[rand.Next(switches.Count)];
                ActivateSwitch(ts);
                if (MadelinePartyModule.IsCelesteNetInstalled()) {
                    CelesteNetSendVector2(ts.Position, 1);
                }
            }
        }

        protected void ActivateSwitch(TouchSwitch ts) {
            switchesOn.Add(ts.Position);
            ts.Visible = true;
            ts.Active = true;
            ts.Collidable = true;
            Add(new Coroutine(ActivateSwitchCoroutine(ts)));
        }

        protected IEnumerator ActivateSwitchCoroutine(TouchSwitch ts) {
            DynamicData switchData = new DynamicData(ts);
            switchData.Get<SoundSource>("touchSfx").Play("event:/game/general/touchswitch_any");
            switchData.Get<Sprite>("icon").Rate = 4;
            float ease = 1;
            while(ease > 0) {
                yield return null;
                switchData.Get<Sprite>("icon").Rate = 1 + 3 * ease;
                ease -= Engine.DeltaTime;
            }
            switchData.Get<Sprite>("icon").Rate = 1;
        }

        protected void DeactivateSwitch(TouchSwitch ts) {
            switchesOn.Remove(ts.Position);
            ts.Visible = false;
            ts.Active = false;
            ts.Collidable = false;
        }

        public override void CelesteNetReceiveVector2(Vector2 vec, int extra) {
            base.CelesteNetReceiveVector2(vec, extra);
            foreach (TouchSwitch ts in switches) {
                if (ts.Position == vec) {
                    if(extra < 0) {
                        DeactivateSwitch(ts);
                        for (int i = 0; i < 32; i++) {
                            float num = Calc.Random.NextFloat((float)Math.PI * 2f);
                            level.Particles.Emit(TouchSwitch.P_FireWhite, ts.Position + Calc.AngleToVector(num, 6f), num);
                        }
                        if (GameData.gnetHost && switchesOn.Count == 0) {
                            NewSwitcheDistribution(ts);
                        }
                    } else {
                        ActivateSwitch(ts);
                    }
                }
            }
        }

        public override void Update() {
            base.Update();
            if (level.RawTimeActive - startTime >= 30 && endCoroutine == null) {
                Add(endCoroutine = new Coroutine(EndMinigame()));
            }
        }

        protected IEnumerator EndMinigame() {
            Player player = level.Tracker.GetEntity<Player>();
            // This check is probably unnecessary, but I left it in for safety
            while (player == null) {
                yield return null;
                player = level.Tracker.GetEntity<Player>();
            }
            completed = true;
            // Freeze the player so they can't do any more collecting until everyone else is done
            player.StateMachine.State = Player.StFrozen;
            player.Speed = Vector2.Zero;
            startTime = -1;
            started = false;
            level.CanRetry = false;
            Console.WriteLine("Touch Switch Count: " + switchCount);
            GameData.minigameResults.Add(new Tuple<int, uint>(GameData.realPlayerID, switchCount));
            if (MadelinePartyModule.IsCelesteNetInstalled()) {
                CelesteNetSendMinigameResults(switchCount);
            }

            // Wait until all players have finished
            while (GameData.minigameResults.Count < GameData.playerNumber) {
                yield return null;
            }

            GameData.minigameResults.Sort((x, y) => { return y.Item2.CompareTo(x.Item2); });

            int winnerID = GameData.minigameResults[0].Item1;
            int realPlayerPlace = GameData.minigameResults.FindIndex((obj) => obj.Item1 == GameData.realPlayerID);
            // A check to stop the game from crashing when I hit one of these while testing
            if (winnerID >= 0 && GameData.players[winnerID] != null) {
                switchCount = 0;
                GameData.players[winnerID].ChangeStrawberries(10);
                level.OnEndOfFrame += delegate {
                    Leader.StoreStrawberries(player.Leader);
                    level.Remove(player);
                    level.UnloadLevel();

                    level.Session.Level = "Game_PlayerRanking";
                    List<Vector2> spawns = new List<Vector2>(level.Session.LevelData.Spawns.ToArray());
                    // Sort the spawns so the highest ones are first
                    spawns.Sort((x, y) => { return x.Y.CompareTo(y.Y); });
                    level.Session.RespawnPoint = level.GetSpawnPoint(new Vector2(spawns[realPlayerPlace].X, spawns[realPlayerPlace].Y));

                    level.LoadLevel(Player.IntroTypes.None);

                    Leader.RestoreStrawberries(player.Leader);
                };
            }
        }
    }
}