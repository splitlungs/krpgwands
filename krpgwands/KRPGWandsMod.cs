
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
//using HarmonyLib;

namespace krpgwands
{
    public class KRPGWandsMod : ModSystem
    {
        public static KRPGWandsMod Instance { get; private set; }
        public KRPGWandsMod()
        {
            if (KRPGWandsMod.Instance == null)
                KRPGWandsMod.Instance = this;
        }

        ~KRPGWandsMod() { }

        public ICoreAPI Api { get; private set; }
        public ICoreClientAPI cApi { get; private set; }
        public ICoreServerAPI sApi { get; private set; }
        
        public WeatherSystemServer wsysServer;

        // public KRPGSystem krpgsystem { get; private set; }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            this.Api = api;

            // krpgsystem = api.ModLoader.GetModSystem<KRPGSystem>();
            // if (krpgsystem == null)
                // api.Logger.Error("KRPG Wands could not load KRPG Lib!");
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            Api = api;

            api.RegisterItemClass("WandItem", typeof(WandItem));
            api.RegisterEntity("MagicProjectileEntity", typeof(MagicProjectileEntity));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            cApi = api;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sApi = api;
            api.Event.GameWorldSave += this.OnGameWorldSave;
            api.Event.PlayerNowPlaying += new PlayerDelegate(this.OnPlayerJoined);
            wsysServer = sApi.ModLoader.GetModSystem<WeatherSystemServer>();
            if (wsysServer == null)
                api.Logger.Fatal("Weather system not loaded!");            
        }

        //public static AssetCategory EffectsAssetCategory = new AssetCategory("enchantments", true, EnumAppSide.Universal);
        
        private void OnGameWorldSave()
        {

        }
        private void OnPlayerJoined(IServerPlayer byPlayer)
        {

        }
    }
}
