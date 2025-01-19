using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace krpgwands
{
    public enum EnumEnchantments { chilling, flaming, frost, harming, healing, knockback, igniting, lightning, pit, shocking }

    /// <summary>
    /// Cloned from ItemBow.cs
    /// </summary>
    public class WandItem : Item
    {
        WorldInteraction[] interactions;

        public static SimpleParticleProperties particles = new SimpleParticleProperties(
                    1, 1,
                    ColorUtil.ColorFromRgba(220, 220, 220, 50),
                    new Vec3d(),
                    new Vec3d(),
                    new Vec3f(-0.25f, 0.1f, -0.25f),
                    new Vec3f(0.25f, 0.1f, 0.25f),
                    1.5f,
                    -0.075f,
                    0.25f,
                    0.25f,
                    EnumParticleModel.Quad
                );

        private float lastParticle = 0f;

        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client) return;
            ICoreClientAPI capi = api as ICoreClientAPI;

            interactions = ObjectCacheUtil.GetOrCreate(api, "wandInteractions", () =>
            {
                List<ItemStack> stacks = new List<ItemStack>();

                return new WorldInteraction[]
                {
                    new WorldInteraction()
                    {
                        ActionLangCode = "heldhelp-wandaim",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = stacks.ToArray()
                    },
                    new WorldInteraction()
                    {
                        ActionLangCode = "heldhelp-wandaim-self",
                        MouseButton = EnumMouseButton.Right,
                        HotKeyCode = EnumModifierKey.SHIFT.ToString(),
                        Itemstacks = stacks.ToArray()
                    }
                };
            });
        }

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
        {
            return null;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            if (handling == EnumHandHandling.PreventDefault) return;

            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

            if (byPlayer.Entity.Controls.Sneak == true && byPlayer.Entity.Controls.CtrlKey == false)
                byEntity.WatchedAttributes.SetInt("aimSelf", 1);
            else
                byEntity.WatchedAttributes.SetInt("aimSelf", 0);

            // Not ideal to code the aiming controls this way. Needs an elegant solution - maybe an event bus?
            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            byEntity.AnimManager.StartAnimation("wandaim");

            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-idle"), byEntity, byPlayer, true, 8);

            lastParticle = 0f;

            handling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            // Make it pretty
            if (byEntity.World is IClientWorldAccessor)
            {
                if (lastParticle + 0.5 < secondsUsed)
                {
                    lastParticle = secondsUsed;

                    Vec3d pos =
                            byEntity.Pos.XYZ.Add(0, byEntity.LocalEyePos.Y, 0)
                            .Ahead(1f, byEntity.Pos.Pitch, byEntity.Pos.Yaw)
                        ;

                    Vec3f speedVec = new Vec3d(0, 0, 0).Ahead(0.15, byEntity.Pos.Pitch, byEntity.Pos.Yaw).ToVec3f();
                    particles.MinVelocity = speedVec;
                    Random rand = new Random();
                    particles.Color = ColorUtil.ColorFromRgba(rand.Next(0, 255), rand.Next(0, 255), rand.Next(0, 255), 180);
                    particles.MinPos = pos.AheadCopy(0.8, byEntity.Pos.Pitch, byEntity.Pos.Yaw);
                    particles.AddPos.Set(0.5, -0.25, 0.1);
                    particles.MinSize = 0.05f;
                    particles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.SINUS, 0.2f);
                    byEntity.World.SpawnParticles(particles);
                }
            }

            return true;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.AnimManager.StopAnimation("wandaim");
            byEntity.WatchedAttributes.SetInt("aimSelf", 0);

            if (cancelReason != EnumItemUseCancelReason.Destroyed)
            {
                (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
            }

            if (cancelReason != EnumItemUseCancelReason.ReleasedMouse)
            {
                byEntity.Attributes.SetInt("aimingCancel", 1);
            }

            return true;
        }
        
        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);

            if (byEntity.Attributes.GetInt("aimingCancel") == 1) return;
            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.AnimManager.StopAnimation("wandaim");

            if (secondsUsed < 0.65f)
            {
                return;
            }

            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), byEntity, byPlayer, false, 8);

            if (byEntity.WatchedAttributes.GetInt("aimSelf", 0) == 1)
            {
                // api.World.Logger.Event("Wand is Aiming Self!");
                return;
            }

            float damage = 0;
            float accuracyBonus = 0f;

            // Base Item damage
            if (slot.Itemstack.Collectible.Attributes != null)
            {
                damage += slot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);

                accuracyBonus = 1 - slot.Itemstack.Collectible.Attributes["accuracyBonus"].AsFloat(0);
            }

            // Get Enchantments
            ITreeAttribute tree = slot.Itemstack.Attributes?.GetOrAddTreeAttribute("enchantments");
            Dictionary<string, int> enchants = new Dictionary<string, int>();
            foreach (var val in Enum.GetValues(typeof(EnumEnchantments)))
            {
                int ePower = tree.GetInt(val.ToString(), 0);
                if (ePower > 0) { enchants.Add(val.ToString(), ePower); }
            }

            EntityProperties type = byEntity.World.GetEntityType(new AssetLocation("krpgwands:magicmissile-temporal"));
            var entitymagicmissile = byEntity.World.ClassRegistry.CreateEntity(type) as EntityProjectile;
            entitymagicmissile.FiredBy = byEntity;
            entitymagicmissile.Damage = damage;
            float acc = Math.Max(0.001f, (1 - byEntity.Attributes.GetFloat("aimingAccuracy", 0)));
            double rndpitch = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1) * acc * (0.75 * accuracyBonus);
            double rndyaw = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1) * acc * (0.75 * accuracyBonus);
            Vec3d pos = byEntity.ServerPos.XYZ.Add(0, byEntity.LocalEyePos.Y, 0);
            Vec3d aheadPos = pos.AheadCopy(1, byEntity.SidedPos.Pitch + rndpitch, byEntity.SidedPos.Yaw + rndyaw);
            Vec3d velocity = (aheadPos - pos) * byEntity.Stats.GetBlended("bowDrawingStrength");
            entitymagicmissile.ServerPos.SetPos(byEntity.SidedPos.BehindCopy(0.21).XYZ.Add(0, byEntity.LocalEyePos.Y, 0));
            entitymagicmissile.ServerPos.Motion.Set(velocity);
            entitymagicmissile.Pos.SetFrom(entitymagicmissile.ServerPos);
            entitymagicmissile.World = byEntity.World;
            entitymagicmissile.SetRotation();

            // Make a new ItemStack to pass Enchantments
            entitymagicmissile.ProjectileStack = new ItemStack();
            // Write to temp stack for Enchantments to process
            // ITreeAttribute newTree = entitymagicmissile.ProjectileStack.Attributes?.GetOrAddTreeAttribute("enchantments");
            foreach (KeyValuePair<string, int> pair in enchants)
            {
                // newTree.SetInt(pair.Key, pair.Value);
                entitymagicmissile.WatchedAttributes.SetInt(pair.Key, pair.Value);
            }
            // entitymagicmissile.ProjectileStack.Attributes.MergeTree(newTree);

            byEntity.World.SpawnEntity(entitymagicmissile);

            slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot);

            byEntity.AnimManager.StartAnimation("bowhit");
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            if (inSlot.Itemstack.Collectible.Attributes != null)
            {
                float num = inSlot.Itemstack.Collectible.Attributes?["damage"].AsFloat() ?? 0f;
                if (num != 0f)
                {
                    dsc.AppendLine(Lang.Get("bow-piercingdamage", num));
                }

                float num2 = inSlot.Itemstack.Collectible?.Attributes["accuracyBonus"].AsFloat() ?? 0f;
                if (num2 != 0f)
                {
                    dsc.AppendLine(Lang.Get("bow-accuracybonus", (num2 > 0f) ? "+" : "", (int)(100f * num2)));
                }
            }

            // float dmg = 0;
            // if (inSlot.Itemstack.Collectible.Attributes["damage"].Exists)
            //    dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();

            // if (dmg != 0) dsc.AppendLine(Lang.Get("krpgenchantment:krpg-magicdamage-arcane", dmg));

            // float accuracyBonus = 0;
            // if (inSlot.Itemstack.Collectible.Attributes["accuracyBonus"].Exists)
            //     accuracyBonus = inSlot.Itemstack.Collectible.Attributes["accuracyBonus"].AsFloat();
            // 
            // if (accuracyBonus != 0) dsc.AppendLine(Lang.Get("bow-accuracybonus", accuracyBonus > 0 ? "+" : "", (int)(100 * accuracyBonus)));
        }
    }
}