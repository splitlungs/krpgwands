
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace krpgwands
{
    public class KRPGWandsMod : ModSystem
    {
        public ICoreAPI Api { get; private set; }
        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
        }
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("WandItem", typeof(WandItem));
        }
    }
}
