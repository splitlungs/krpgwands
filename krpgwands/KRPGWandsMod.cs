using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System;

namespace KRPGLib.Wands
{
    public class KRPGWandsMod : ModSystem
    {
        
        public ICoreAPI Api { get; private set; }
        public delegate void WandDamageDelegate(Entity target, DamageSource damageSource, ItemSlot slot, ref float damage);
        public event WandDamageDelegate OnDealWandDamage;
        public bool DealDamage(Entity target, DamageSource damageSource, ItemSlot slot, float damage)
        {
            if (OnDealWandDamage != null)
            {
                OnDealWandDamage?.Invoke(target, damageSource, slot, ref damage);
            }
            return target.ReceiveDamage(damageSource, damage);
        }
        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
        }
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterItemClass("WandItem", typeof(WandItem));
            api.RegisterEntity("MagicProjectile", typeof(MagicProjectile));
        }
    }
}
