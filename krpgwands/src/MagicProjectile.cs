using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static System.Net.Mime.MediaTypeNames;

namespace KRPGLib.Wands
{
    public class MagicProjectile : Entity
    {
        private bool beforeCollided;
        private bool stuck;
        private long msLaunch;
        private long msCollide;
        private Vec3d motionBeforeCollide = new Vec3d();
        private CollisionTester collTester = new CollisionTester();
        public Entity FiredBy;
        protected long FiredByMountEntityId;
        public float Weight = 0.1f;
        public float Damage;
        public int DamageTier;
        public EnumDamageType DamageType;
        public ItemSlot SourceSlot;
        public ItemStack ProjectileStack;
        public float DropOnImpactChance;
        public bool DamageStackOnImpact;
        private Cuboidf collisionTestBox;
        private EntityPartitioning ep;
        protected List<long> entitiesHit = new List<long>();
        public bool EntityHit { get; protected set; }

        public override bool ApplyGravity => !stuck;

        public override bool IsInteractable => false;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            if (Api.Side == EnumAppSide.Server && FiredBy != null)
            {
                WatchedAttributes.SetLong("firedBy", FiredBy.EntityId);
            }

            if (Api.Side == EnumAppSide.Client)
            {
                FiredBy = Api.World.GetEntityById(WatchedAttributes.GetLong("firedBy", 0L));
            }

            msLaunch = World.ElapsedMilliseconds;
            if (FiredBy != null && FiredBy is EntityAgent entityAgent && entityAgent.MountedOn?.Entity != null)
            {
                FiredByMountEntityId = entityAgent.MountedOn.Entity.EntityId;
            }

            collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);
            GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = onPhysicsTickCallback;
            ep = api.ModLoader.GetModSystem<EntityPartitioning>();
            GetBehavior<EntityBehaviorPassivePhysics>().CollisionYExtra = 0f;
        }
        private void onPhysicsTickCallback(float dtFac)
        {
            if (ShouldDespawn || !Alive || World.ElapsedMilliseconds <= msCollide + 500)
            {
                return;
            }

            EntityPos sidedPos = base.SidedPos;
            if (sidedPos.Motion.LengthSq() < 0.040000000000000008)
            {
                return;
            }

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(sidedPos.X, sidedPos.Y, sidedPos.Z);
            if (sidedPos.Motion.X < 0.0)
            {
                projectileBox.X1 += sidedPos.Motion.X * (double)dtFac;
            }
            else
            {
                projectileBox.X2 += sidedPos.Motion.X * (double)dtFac;
            }

            if (sidedPos.Motion.Y < 0.0)
            {
                projectileBox.Y1 += sidedPos.Motion.Y * (double)dtFac;
            }
            else
            {
                projectileBox.Y2 += sidedPos.Motion.Y * (double)dtFac;
            }

            if (sidedPos.Motion.Z < 0.0)
            {
                projectileBox.Z1 += sidedPos.Motion.Z * (double)dtFac;
            }
            else
            {
                projectileBox.Z2 += sidedPos.Motion.Z * (double)dtFac;
            }

            ep.WalkEntities(sidedPos.XYZ, 5.0, delegate (Entity e)
            {
                if (e.EntityId == EntityId || (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500) || !e.IsInteractable)
                {
                    return true;
                }

                if (entitiesHit.Contains(e.EntityId))
                {
                    return false;
                }

                if (e.EntityId == FiredByMountEntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return true;
                }

                if (e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z).IntersectsOrTouches(projectileBox))
                {
                    impactOnEntity(e);
                    return false;
                }

                return true;
            }, EnumEntitySearchType.Creatures);
        }
        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (ShouldDespawn)
            {
                return;
            }

            EntityPos sidedPos = base.SidedPos;
            stuck = base.Collided || collTester.IsColliding(World.BlockAccessor, collisionTestBox, sidedPos.XYZ) || WatchedAttributes.GetBool("stuck");
            if (Api.Side == EnumAppSide.Server)
            {
                WatchedAttributes.SetBool("stuck", stuck);
            }

            double impactSpeed = Math.Max(motionBeforeCollide.Length(), sidedPos.Motion.Length());
            if (stuck)
            {
                if (Api.Side == EnumAppSide.Client)
                {
                    ServerPos.SetFrom(Pos);
                }

                IsColliding(sidedPos, impactSpeed);
                entitiesHit.Clear();
            }
            else
            {
                SetRotation();
                if (!TryAttackEntity(impactSpeed))
                {
                    beforeCollided = false;
                    motionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
                }
            }
        }
        public override void OnCollided()
        {
            EntityPos sidedPos = base.SidedPos;
            IsColliding(base.SidedPos, Math.Max(motionBeforeCollide.Length(), sidedPos.Motion.Length()));
            motionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
        }

        private void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0.0, 0.0, 0.0);
            if (beforeCollided || !(World is IServerWorldAccessor) || World.ElapsedMilliseconds <= msCollide + 500)
            {
                return;
            }

            World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, randomizePitch: false);

            TryAttackEntity(impactSpeed);
            msCollide = World.ElapsedMilliseconds;
            beforeCollided = true;

            Die();
        }

        private bool TryAttackEntity(double impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250)
            {
                return false;
            }

            if (impactSpeed <= 0.01)
            {
                return false;
            }

            _ = base.SidedPos;
            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);
            if (ServerPos.Motion.X < 0.0)
            {
                projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            }
            else
            {
                projectileBox.X2 += 1.5 * ServerPos.Motion.X;
            }

            if (ServerPos.Motion.Y < 0.0)
            {
                projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            }
            else
            {
                projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;
            }

            if (ServerPos.Motion.Z < 0.0)
            {
                projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            }
            else
            {
                projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;
            }

            Entity nearestEntity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, delegate (Entity e)
            {
                if (e.EntityId == EntityId || !e.IsInteractable)
                {
                    return false;
                }

                return (FiredBy == null || e.EntityId != FiredBy.EntityId || World.ElapsedMilliseconds - msLaunch >= 500) && e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z).IntersectsOrTouches(projectileBox);
            });
            if (nearestEntity != null)
            {
                impactOnEntity(nearestEntity);
                return true;
            }

            return false;
        }

        private void impactOnEntity(Entity entity)
        {
            if (!Alive)
            {
                return;
            }

            EntityPos sidedPos = base.SidedPos;
            IServerPlayer serverPlayer = null;
            if (FiredBy is EntityPlayer)
            {
                serverPlayer = (FiredBy as EntityPlayer).Player as IServerPlayer;
            }

            bool flag = entity is EntityPlayer;
            bool flag2 = entity is EntityAgent;
            bool flag3 = true;
            ICoreServerAPI coreServerAPI = World.Api as ICoreServerAPI;
            if (serverPlayer != null)
            {
                if (flag && (!coreServerAPI.Server.Config.AllowPvP || !serverPlayer.HasPrivilege("attackplayers")))
                {
                    flag3 = false;
                }

                if (flag2 && !serverPlayer.HasPrivilege("attackcreatures"))
                {
                    flag3 = false;
                }
            }
            msCollide = World.ElapsedMilliseconds;
            sidedPos.Motion.Set(0.0, 0.0, 0.0);
            if (flag3 && World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(new AssetLocation("sounds/arrow-impact"), this, null, randomizePitch: false, 24f);
                float num = Damage;
                if (FiredBy != null)
                {
                    num *= FiredBy.Stats.GetBlended("rangedWeaponsDamage");
                }

                DamageSource damageSource = new DamageSource
                {
                    Source = ((serverPlayer != null) ? EnumDamageSource.Player : EnumDamageSource.Entity),
                    SourceEntity = this,
                    CauseEntity = FiredBy,
                    Type = EnumDamageType.PiercingAttack
                };

                KRPGWandsMod wands = Api.ModLoader.GetModSystem<KRPGWandsMod>();
                bool flag4 = wands.DealDamage(entity, damageSource, SourceSlot, num);

                float knockbackResistance = entity.Properties.KnockbackResistance;
                entity.SidedPos.Motion.Add((double)knockbackResistance * sidedPos.Motion.X * (double)Weight, (double)knockbackResistance * sidedPos.Motion.Y * (double)Weight, (double)knockbackResistance * sidedPos.Motion.Z * (double)Weight);
                int num2 = 1;
                if (DamageStackOnImpact)
                {
                    ProjectileStack.Collectible.DamageItem(entity.World, entity, new DummySlot(ProjectileStack));
                    num2 = ((ProjectileStack == null) ? 1 : ProjectileStack.Collectible.GetRemainingDurability(ProjectileStack));
                }

                if (!(World.Rand.NextDouble() < (double)DropOnImpactChance) || num2 <= 0)
                {
                    Die();
                }

                if (FiredBy is EntityPlayer && flag4)
                {
                    World.PlaySoundFor(new AssetLocation("sounds/player/projectilehit"), (FiredBy as EntityPlayer).Player, randomizePitch: false, 24f);
                }
            }
        }



        public virtual void SetRotation()
        {
            EntityPos entityPos = ((World is IServerWorldAccessor) ? ServerPos : Pos);
            double num = entityPos.Motion.Length();
            if (num > 0.01)
            {
                entityPos.Pitch = 0f;
                entityPos.Yaw = MathF.PI + (float)Math.Atan2(entityPos.Motion.X / num, entityPos.Motion.Z / num) + GameMath.Cos((float)(World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f;
                entityPos.Roll = 0f - (float)Math.Asin(GameMath.Clamp((0.0 - entityPos.Motion.Y) / num, -1.0, 1.0)) + GameMath.Sin((float)(World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f;
            }
        }

        public override bool CanCollect(Entity byEntity)
        {
            return false;
        }

        public override void OnCollideWithLiquid()
        {
            base.OnCollideWithLiquid();
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            ProjectileStack.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();
            ProjectileStack = new ItemStack(reader);
        }
    }
}