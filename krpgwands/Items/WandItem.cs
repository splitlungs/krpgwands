using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace krpgwands
{
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
                        ActionLangCode = "heldhelp-chargewand",
                        MouseButton = EnumMouseButton.Right,
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

            // Not ideal to code the aiming controls this way. Needs an elegant solution - maybe an event bus?
            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            byEntity.AnimManager.StartAnimation("bowaim");

            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-idle"), byEntity, byPlayer, true, 8);
            
            handling = EnumHandHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            int num = GameMath.Clamp((int)Math.Ceiling(secondsUsed * 4f), 0, 3);
            int @int = slot.Itemstack.Attributes.GetInt("renderVariant");
            slot.Itemstack.TempAttributes.SetInt("renderVariant", num);
            slot.Itemstack.Attributes.SetInt("renderVariant", num);
            if (@int != num)
            {
                (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
            }

            // Make it pretty
            if (byEntity.World is IClientWorldAccessor)
            {
                if (secondsUsed > 0.6)
                {
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
            byEntity.AnimManager.StopAnimation("bowaim");

            if (byEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.TempAttributes.RemoveAttribute("renderVariant");
            }

            slot.Itemstack?.Attributes.SetInt("renderVariant", 0);
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
            if (byEntity.Attributes.GetInt("aimingCancel") == 1) return;
            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.AnimManager.StopAnimation("bowaim");

            if (byEntity.World is IClientWorldAccessor)
            {
                slot.Itemstack.TempAttributes.RemoveAttribute("renderVariant");
            }

            slot.Itemstack.Attributes.SetInt("renderVariant", 0);
            (byEntity as EntityPlayer)?.Player?.InventoryManager.BroadcastHotbarSlot();
            if (secondsUsed < 0.65f)
            {
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

            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), byEntity, byPlayer, false, 8);


            // TODO: Make different projectile entities for different wands
            // EntityProperties type = byEntity.World.GetEntityType(new AssetLocation("krpgwands:magicmissile-" + slot.Itemstack.Collectible.Variant["material"]));
            EntityProperties type = byEntity.World.GetEntityType(new AssetLocation("krpgwands:magicmissile-" + "temporal"));
            var entitymagicmissile = byEntity.World.ClassRegistry.CreateEntity(type) as MagicProjectileEntity;
            entitymagicmissile.FiredBy = byEntity;
            entitymagicmissile.Damage = damage;

            // Enchantments
            int power = 0;
            power = slot.Itemstack.Attributes.GetInt("chilling", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("chilling", power);
            power = slot.Itemstack.Attributes.GetInt("flaming", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("flaming", power);
            power = slot.Itemstack.Attributes.GetInt("frost", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("frost", power);
            power = slot.Itemstack.Attributes.GetInt("harming", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("harming", power);
            power = slot.Itemstack.Attributes.GetInt("healing", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("healing", power);
            power = slot.Itemstack.Attributes.GetInt("knockback", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("knockback", power);
            power = slot.Itemstack.Attributes.GetInt("igniting", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("igniting", power);
            power = slot.Itemstack.Attributes.GetInt("lightning", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("lightning", power);
            power = slot.Itemstack.Attributes.GetInt("shocking", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("shocking", power);
            power = slot.Itemstack.Attributes.GetInt("pit", 0);
            if (power > 0) entitymagicmissile.WatchedAttributes.SetInt("pit", power);
            
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

            byEntity.World.SpawnEntity(entitymagicmissile);

            slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot);

            byEntity.AnimManager.StartAnimation("bowhit");
        }


        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot.Itemstack.Collectible.Attributes == null) return;

            float dmg = 0;
            if (inSlot.Itemstack.Collectible.Attributes["damage"].Exists)
                dmg = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();

            if (dmg != 0) dsc.AppendLine(Lang.Get("krpgenchantment:krpg-magicdamage-arcane", dmg));

            //float dmg = inSlot.Itemstack.Collectible.Attributes?["damage"].AsFloat(0) ?? 0;
            //if (dmg != 0) dsc.AppendLine(Lang.Get("bow-piercingdamage", dmg));

            float accuracyBonus = 0;
            if (inSlot.Itemstack.Collectible.Attributes["accuracyBonus"].Exists)
                accuracyBonus = inSlot.Itemstack.Collectible.Attributes["accuracyBonus"].AsFloat();
            
            if (accuracyBonus != 0) dsc.AppendLine(Lang.Get("bow-accuracybonus", accuracyBonus > 0 ? "+" : "", (int)(100 * accuracyBonus)));
            
            //float accuracyBonus = inSlot.Itemstack.Collectible?.Attributes["accuracyBonus"].AsFloat(0) ?? 0;
            //if (accuracyBonus != 0) dsc.AppendLine(Lang.Get("bow-accuracybonus", accuracyBonus > 0 ? "+" : "", (int)(100 * accuracyBonus)));
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return interactions.Append(base.GetHeldInteractionHelp(inSlot));
        }
    }
}