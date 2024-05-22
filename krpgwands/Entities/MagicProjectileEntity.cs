using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace krpgwands
{
    /// <summary>
    /// Cloned from EntityProjectile.cs
    /// </summary>
    public class MagicProjectileEntity : Entity
    {
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
        
        public IServerNetworkChannel serverChannel;

        bool beforeCollided;
        bool stuck;

        long msLaunch;
        long msCollide;

        Vec3d motionBeforeCollide = new Vec3d();

        CollisionTester collTester = new CollisionTester();

        public Entity FiredBy;
        public float Weight = 0.1f;
        public float Damage;

        Cuboidf collisionTestBox;

        EntityPartitioning ep;


        #region Default Class
        public override bool ApplyGravity
        {
            get { return !stuck; }
        }

        public override bool IsInteractable
        {
            get { return false; }
        }
        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);

            msLaunch = World.ElapsedMilliseconds;
            collisionTestBox = SelectionBox.Clone().OmniGrowBy(0.05f);
            GetBehavior<EntityBehaviorPassivePhysics>().OnPhysicsTickCallback = onPhysicsTickCallback;
            ep = api.ModLoader.GetModSystem<EntityPartitioning>();
            GetBehavior<EntityBehaviorPassivePhysics>().collisionYExtra = 0f;
        }

        private void onPhysicsTickCallback(float dtFac)
        {
            if (ShouldDespawn || !Alive || World.ElapsedMilliseconds <= msCollide + 500)
            {
                return;
            }

            EntityPos sidedPos = base.SidedPos;
            if (sidedPos.Motion.X == 0.0 && sidedPos.Motion.Y == 0.0 && sidedPos.Motion.Z == 0.0)
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
                return;
            }

            SetRotation();
            if (!TryAttackEntity(impactSpeed))
            {
                beforeCollided = false;
                motionBeforeCollide.Set(sidedPos.Motion.X, sidedPos.Motion.Y, sidedPos.Motion.Z);
            }

            if (Api.Side == EnumAppSide.Client && !stuck)
            {
                Random rand = new Random();
                particles.Color = ColorUtil.ColorFromRgba(rand.Next(0, 255), rand.Next(0, 255), rand.Next(0, 255), 255);
                particles.MinPos = sidedPos.AheadCopy(0.8d).XYZ;
                particles.AddPos.Set(0.1, 0.1, 0.1);
                particles.MinSize = 0.1f;
                particles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.SINUS, 1);
                this.Api.World.SpawnParticles(particles);
            }
        }


        public override void OnCollided()
        {
            EntityPos pos = SidedPos;

            IsColliding(SidedPos, Math.Max(motionBeforeCollide.Length(), pos.Motion.Length()));
            motionBeforeCollide.Set(pos.Motion.X, pos.Motion.Y, pos.Motion.Z);
        }

        private void IsColliding(EntityPos pos, double impactSpeed)
        {
            pos.Motion.Set(0, 0, 0);

            if (!beforeCollided && World is IServerWorldAccessor && World.ElapsedMilliseconds > msCollide + 500)
            {
                if (impactSpeed >= 0.07)
                {
                    World.PlaySoundAt(new AssetLocation("game:sounds/arrow-impact"), this, null, false, 32);
                    
                    // Resend position to client
                    WatchedAttributes.MarkAllDirty();
                    
                    Die();
                }

                TryAttackEntity(impactSpeed);

                msCollide = World.ElapsedMilliseconds;

                beforeCollided = true;
            }

            Die();
        }

        bool TryAttackEntity(double impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= 0.01) return false;

            EntityPos pos = SidedPos;

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);

            // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick 
            if (ServerPos.Motion.X < 0) projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            else projectileBox.X2 += 1.5 * ServerPos.Motion.X;
            if (ServerPos.Motion.Y < 0) projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            else projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;
            if (ServerPos.Motion.Z < 0) projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            else projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;

            Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                if (e.EntityId == this.EntityId || !e.IsInteractable) return false;

                if (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return false;
                }

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                return eBox.IntersectsOrTouches(projectileBox);
            });

            if (entity != null)
            {
                impactOnEntity(entity);
                return true;
            }

            return false;
        }

        private void impactOnEntity(Entity entity)
        {
            if (!Alive) return;

            EntityPos pos = SidedPos;

            IServerPlayer fromPlayer = null;
            if (FiredBy is EntityPlayer)
            {
                fromPlayer = (FiredBy as EntityPlayer).Player as IServerPlayer;
            }

            bool targetIsPlayer = entity is EntityPlayer;
            bool targetIsCreature = entity is EntityAgent;
            bool canDamage = true;

            ICoreServerAPI sapi = World.Api as ICoreServerAPI;
            if (fromPlayer != null)
            {
                if (targetIsPlayer && (!sapi.Server.Config.AllowPvP || !fromPlayer.HasPrivilege("attackplayers"))) canDamage = false;
                if (targetIsCreature && !fromPlayer.HasPrivilege("attackcreatures")) canDamage = false;
            }

            msCollide = World.ElapsedMilliseconds;

            if (canDamage && World.Side == EnumAppSide.Server)
            {
                World.PlaySoundAt(new AssetLocation("game:sounds/arrow-impact"), this, null, false, 24);

                // Alternate Damage
                int flaming = WatchedAttributes.GetInt("flaming", 0);
                int frost = WatchedAttributes.GetInt("frost", 0);
                int harming = WatchedAttributes.GetInt("harming", 0);
                int healing = WatchedAttributes.GetInt("healing", 0);
                int shocking = WatchedAttributes.GetInt("shocking", 0);

                bool didDamage = false;
                // Healing
                if (healing > 0)
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    dSource.Type = EnumDamageType.Heal;
                    float dmg = World.Rand.Next(1, 6) + healing;
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Base
                else
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    float dmg = Damage;
                    if (FiredBy != null) dmg *= FiredBy.Stats.GetBlended("rangedWeaponsDamage");
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Flaming
                if (flaming > 0)
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    dSource.Type = EnumDamageType.Fire;
                    float dmg = World.Rand.Next(1, 6) + flaming;
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Frost
                if (frost > 0)
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    dSource.Type = EnumDamageType.Frost;
                    float dmg = World.Rand.Next(1, 6) + frost;
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Harming
                if (harming > 0)
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    dSource.Type = EnumDamageType.Injury;
                    float dmg = World.Rand.Next(1, 6) + harming;
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Shocking
                if (shocking > 0)
                {
                    DamageSource dSource = new DamageSource();
                    dSource.Source = EnumDamageSource.Entity;
                    dSource.SourceEntity = FiredBy == null ? this : FiredBy;
                    dSource.Type = EnumDamageType.Electricity;
                    float dmg = World.Rand.Next(1, 6) + shocking;
                    didDamage = entity.ReceiveDamage(dSource, dmg);
                }
                // Base Knockback
                float kbresist = entity.Properties.KnockbackResistance;
                entity.SidedPos.Motion.Add(kbresist * SidedPos.X * Weight, kbresist * SidedPos.Y * Weight, kbresist * SidedPos.Z * Weight);

                int power = 0;
                // Chilling
                power = WatchedAttributes.GetInt("chilling", 0);
                if (power > 0) ChillEntity(entity, power);
                // Ignite
                power = WatchedAttributes.GetInt("igniting", 0);
                if (power > 0) IgniteEntity(entity);
                // Knockback
                power = WatchedAttributes.GetInt("knockback", 0);
                if (power > 0)
                {
                    double weightedPower = Weight + power * 100;
                    entity.SidedPos.Motion.Mul(-weightedPower, 1, -weightedPower);
                    //entity.SidedPos.Motion.Add(kbresist * SidedPos.X * weightedPower, kbresist * SidedPos.Y, kbresist * SidedPos.Z * weightedPower);
                }
                // Lightning
                power = WatchedAttributes.GetInt("lightning", 0);
                if (power > 0) CallLightning(pos);
                // Pit
                power = WatchedAttributes.GetInt("pit", 0);
                if (power > 0) CreatePit(pos, entity, power, power);

                if (FiredBy is EntityPlayer)
                {
                    World.PlaySoundFor(new AssetLocation("game:sounds/player/projectilehit"), (FiredBy as EntityPlayer).Player, false, 24);
                }
                entity.WatchedAttributes.MarkAllDirty();
            }
            pos.Motion.Set(0, 0, 0);
            Die();
        }

        public virtual void SetRotation()
        {
            EntityPos pos = (World is IServerWorldAccessor) ? ServerPos : Pos;

            double speed = pos.Motion.Length();

            if (speed > 0.01)
            {
                pos.Pitch = 0;
                pos.Yaw =
                    GameMath.PI + (float)Math.Atan2(pos.Motion.X / speed, pos.Motion.Z / speed)
                    + GameMath.Cos((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
                pos.Roll =
                    -(float)Math.Asin(GameMath.Clamp(-pos.Motion.Y / speed, -1, 1))
                    + GameMath.Sin((World.ElapsedMilliseconds - msLaunch) / 200f) * 0.03f
                ;
            }
        }

        public override void OnCollideWithLiquid()
        {
            base.OnCollideWithLiquid();
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);
            writer.Write(beforeCollided);
            //ProjectileStack.ToBytes(writer);
        }

        public override void FromBytes(BinaryReader reader, bool fromServer)
        {
            base.FromBytes(reader, fromServer);
            beforeCollided = reader.ReadBoolean();
            //ProjectileStack = new ItemStack(reader);
        }
        #endregion
        #region Effects
        // Not in use yet, but probably should crustomize collision processing
        public bool TryEffects(Entity byEntity, float impactSpeed)
        {
            if (World is IClientWorldAccessor || World.ElapsedMilliseconds <= msCollide + 250) return false;
            if (impactSpeed <= 0.01) return false;

            EntityPos pos = SidedPos;

            Cuboidd projectileBox = SelectionBox.ToDouble().Translate(ServerPos.X, ServerPos.Y, ServerPos.Z);

            // We give it a bit of extra leeway of 50% because physics ticks can run twice or 3 times in one game tick 
            if (ServerPos.Motion.X < 0) projectileBox.X1 += 1.5 * ServerPos.Motion.X;
            else projectileBox.X2 += 1.5 * ServerPos.Motion.X;
            if (ServerPos.Motion.Y < 0) projectileBox.Y1 += 1.5 * ServerPos.Motion.Y;
            else projectileBox.Y2 += 1.5 * ServerPos.Motion.Y;
            if (ServerPos.Motion.Z < 0) projectileBox.Z1 += 1.5 * ServerPos.Motion.Z;
            else projectileBox.Z2 += 1.5 * ServerPos.Motion.Z;

            Entity entity = World.GetNearestEntity(ServerPos.XYZ, 5f, 5f, (e) => {
                if (e.EntityId == this.EntityId || !e.IsInteractable) return false;

                if (FiredBy != null && e.EntityId == FiredBy.EntityId && World.ElapsedMilliseconds - msLaunch < 500)
                {
                    return false;
                }

                Cuboidd eBox = e.SelectionBox.ToDouble().Translate(e.ServerPos.X, e.ServerPos.Y, e.ServerPos.Z);

                return eBox.IntersectsOrTouches(projectileBox);
            });

            if (entity != null)
            {
                impactOnEntity(entity);
                return true;
            }


            return false;
        }
        public void ChillEntity(Entity entity, int power)
        {
            EntityBehaviorBodyTemperature ebbt = entity.GetBehavior<EntityBehaviorBodyTemperature>();

            // If we encounter something without one, bail
            if (ebbt == null)
                return;

            ebbt.CurBodyTemperature = power * -10f;

        }
        public void CreatePit(EntityPos pos, Entity byEntity, int depth, int width)
        {
            BlockPos bpos = pos.AsBlockPos;
            List<Vec3d> pitArea = new List<Vec3d>();

            for (int x = 0; x <= width; x++)
            {
                for (int y = 0; y <= depth; y++)
                {
                    for (int z = 0; z <= width; z++)
                    {
                        pitArea.Add(new Vec3d(bpos.X + x, bpos.Y - y, bpos.Z + z));
                        pitArea.Add(new Vec3d(bpos.X - x, bpos.Y - y, bpos.Z - z));
                        pitArea.Add(new Vec3d(bpos.X + x, bpos.Y - y, bpos.Z - z));
                        pitArea.Add(new Vec3d(bpos.X - x, bpos.Y - y, bpos.Z + z));
                    }
                }
            }

            for (int i = 0; i < pitArea.Count; i++)
            {
                BlockPos ipos = bpos;
                ipos.Set(pitArea[i]);
                Block block = World.BlockAccessor.GetBlock(ipos);

                if (block != null)
                {
                    string blockCode = block.Code.ToString();
                    if (blockCode.Contains("soil") || blockCode.Contains("sand") || blockCode.Contains("gravel"))
                        World.BlockAccessor.BreakBlock(ipos, byEntity as IPlayer);
                }
            }
        }
        public void IgniteEntity(Entity entity)
        {
            entity.IsOnFire = true;
        }
        public void CallLightning(EntityPos pos)
        {
            WeatherSystemServer weatherSystem = World.Api.ModLoader.GetModSystem<WeatherSystemServer>();
            // It should default to 0f. Stun should stop at 0.5. Absorbtion should start at 1f.
            weatherSystem.SpawnLightningFlash(pos.XYZ);
        }
        #endregion
    }
}