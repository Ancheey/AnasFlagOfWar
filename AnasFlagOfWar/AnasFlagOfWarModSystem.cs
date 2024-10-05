using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace AnasFlagOfWar
{
    public class AnasFlagOfWarModSystem : ModSystem
    {
        public static bool EnableSystem = true;
        public static bool PbPvp = true;
        public override void StartServerSide(ICoreServerAPI api)
        {

            api.RegisterEntityBehaviorClass("EntityBehaviorPvp", typeof(EntityBehaviorPvp));
            var pvp = api.ChatCommands
                .Create("pvp").RequiresPlayer()
                .WithDescription("Displays personal pvp settings")
                .RequiresPrivilege(Privilege.chat)
                .HandleWith((args) =>
                {
                    var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                    if(pvp is null)
                        return TextCommandResult.Error("no pvp behavior");
                    StringBuilder sb = new();

                    if (pvp.DueledEntityPlayer is IPlayer p)
                        sb.AppendLine($"You're currently challenging {p.PlayerName}");

                    if (pvp.BattleCode != "")
                        sb.AppendLine($"You're taking part in a private battle");

                    if (pvp.FlagOfWar)
                        sb.Append($"Your flag of war is up!");
                    else
                        sb.Append($"Your flag of war is lowered");

                    return TextCommandResult.Success(sb.ToString());
                });
            pvp.BeginSubCommand("on").IgnoreAdditionalArgs().WithDescription("Raises your Flag of War, flagging you for FFA pvp").HandleWith((args) =>
            {
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                if (pvp.FlagOfWar)
                    return TextCommandResult.Success("Your flag of war is already up!");
                pvp.FlagOfWar = true;
                return TextCommandResult.Success("You have been flaged for war!");
            });
            pvp.BeginSubCommand("off").IgnoreAdditionalArgs().WithDescription("Lowers your Flag of War").HandleWith((args) =>
            {
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                if (!pvp.FlagOfWar)
                    return TextCommandResult.Success("Your flag of war is already lowered. Can't get more cowardly than that!");
                pvp.FlagOfWar = false;
                return TextCommandResult.Success("You have lowered the flag of war");
            });
            pvp.BeginSubCommand("battle").WithArgs(api.ChatCommands.Parsers.Word("password"))
                .WithDescription("Enables pvp with people who have joined the same battle")
                .HandleWith((args) => 
            {
                if (args.ArgCount == 0)
                    return TextCommandResult.Error("You need to provide a password");
                string password = args.Parsers[0].GetValue().ToString();
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                pvp.BattleCode = password;
                var players = api.World.AllOnlinePlayers.Count(p => p.Entity.GetBehavior<EntityBehaviorPvp>().BattleCode == password);
                return TextCommandResult.Success($"You've joined a battle with {players-1} other {(players-1==1?"person":"people")}");
            });
            pvp.BeginSubCommand("duel").WithArgs(api.ChatCommands.Parsers.OnlinePlayer("player"))
                .WithDescription("Sends a challenge to a designated player or accepts it if challenged")
                .HandleWith((args) =>
            {
                if (args.ArgCount == 0)
                    return TextCommandResult.Error("You need to name your challenger");

                var challenger = ((OnlinePlayerArgParser)args.Parsers[0]).GetValue() as IPlayer;


                var challengerpvp = challenger.Entity.GetBehavior<EntityBehaviorPvp>();
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();

                //Challenging
                if (pvp.DueledEntityPlayer != challenger && challengerpvp.DueledEntityPlayer != args.Caller.Player)
                {
                    pvp.DueledEntityPlayer = challenger;
                    api.SendMessage(challenger, GlobalConstants.CurrentChatGroup, $"{args.Caller.Player.PlayerName} has challenged you to a duel. /pvp duel {args.Caller.Player.PlayerName} to accept.", EnumChatType.OwnMessage);
                    return TextCommandResult.Success($"Issued a challenge to {challenger.PlayerName}");
                }
                //Accepting a challenge
                else if(pvp.DueledEntityPlayer != challenger)
                {
                    pvp.DueledEntityPlayer = challenger;
                    api.SendMessage(challenger, GlobalConstants.CurrentChatGroup, $"{args.Caller.Player.PlayerName} has accepted your challenge", EnumChatType.OwnMessage);
                    return TextCommandResult.Success($"Issued a challenge to {challenger.PlayerName}");
                }
                else if(challenger == args.Caller.Player)
                {
                    return TextCommandResult.Success($"You can't challenge yourself to a duel");
                }
                else
                {
                    return TextCommandResult.Success($"You've already challenged {challenger.PlayerName}. Wait for their response.");
                }
            });
            var forfeit = pvp.BeginSubCommand("forfeit").WithDescription("battle/duel | Removes you from a battle or withdraws your challenge");
            forfeit.BeginSubCommand("battle").WithDescription("Leaves a battle").HandleWith((args) =>
            {
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                if(pvp.BattleCode == "")
                    return TextCommandResult.Success("You're not a part of any battle");
                pvp.BattleCode = "";
                return TextCommandResult.Success("You've left a battle");
            });
            forfeit.BeginSubCommand("duel").WithDescription("Withdraws your challenge")
                .HandleWith((args) =>
            {
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                if (pvp.DueledEntityUID == "")
                    return TextCommandResult.Success("You've not issued a challenge");
                pvp.DueledEntityUID = "";
                return TextCommandResult.Success("You withdrew your challenge");
            });
            pvp.BeginSubCommand("msg").WithDescription("Disables info messages").HandleWith((args) =>
            {
                var pvp = args.Caller.Player.Entity.GetBehavior<EntityBehaviorPvp>();
                var a = (pvp.GetMessages = !pvp.GetMessages);
                return TextCommandResult.Success(a ? "Enabled pvp info messages" : "Disabled pvp info messages");
            });
            base.StartServerSide(api);
        }
    }
    public class EntityBehaviorPvp : EntityBehavior
    {
        public static readonly string PVPFLAGTREE_ATTRIBUTE_TREE_NAME = "anaspvpflags";
        /// <summary>
        /// entity.watchedattributes holds the ID of the dueled player
        /// /pvp duel [name]    - this will issue a pvp duel to the player who has to use the same command to agree to the duel
        /// /pvp chicken        - This will disable the duel without dying (Maybe play a sound of a chicken for funsies)
        /// if you die, this id is reset to an empty string
        /// </summary>
        public static readonly string PVPDUEL_ATTRIBUTE = "pvpduel";

        /// <summary>
        /// This is a string variable which holds a private key. You're only able to get struck by others with this code (Multi-person duel)
        /// /pvp battle [code]  - Turn on the flag
        /// /pvp battle         - Turn off the flag
        /// This will NOT reset until the player does it himself
        /// </summary>
        public static readonly string PVPBATTLE_ATTRIBUTE = "pvpbattlecode";

        /// <summary>
        /// This is a boolean value which marks if you can attack or be attacked by other players with the flag of war
        /// This is discriminatory and will ONLY allow you to fight others with the flag of war
        /// /pvp on/off
        /// </summary>
        public static readonly string PVPWAR_ATTRIBUTE = "pvpflagofwar";

        /// <summary>
        /// Whether the player wants to recive notifications that he can't hit the enemy.
        /// </summary>
        public static readonly string MSG_ATTRIBUTE = "pvpmessage";
        /// <summary>
        /// This class should only be registered when the server starts with pvp off
        /// </summary>
        /// <param name="entity"></param>
        public EntityBehaviorPvp(Entity entity) : base(entity) { }

        ITreeAttribute PvpTree => entity.WatchedAttributes.GetTreeAttribute(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
        public IPlayer DueledEntityPlayer
        {
            get
            {
                string entityuid = PvpTree.GetString(PVPDUEL_ATTRIBUTE, "");
                if (entityuid == "")
                    return null;
                return entity.World.PlayerByUid(entityuid);
            }
            set
            {
                PvpTree.SetString(PVPDUEL_ATTRIBUTE, value.PlayerUID);
                entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
            }
        }
        public string DueledEntityUID
        {
            get => PvpTree.GetString(PVPDUEL_ATTRIBUTE, "");
            set
            {
                PvpTree.SetString(PVPDUEL_ATTRIBUTE, value);
                PvpTree.SetString(PVPBATTLE_ATTRIBUTE, "");
                PvpTree.SetBool(PVPWAR_ATTRIBUTE, false);
                entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
            }
        }
        public string BattleCode
        {
            get => PvpTree.GetString(PVPBATTLE_ATTRIBUTE, "");
            set
            {
                PvpTree.SetString(PVPBATTLE_ATTRIBUTE, value);
                PvpTree.SetBool(PVPWAR_ATTRIBUTE, false);
                PvpTree.SetString(PVPDUEL_ATTRIBUTE, "");
                entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
            }
        }
        public bool FlagOfWar
        {
            get => PvpTree.GetBool(PVPWAR_ATTRIBUTE, false);
            set
            {        
                PvpTree.SetBool(PVPWAR_ATTRIBUTE, value);
                PvpTree.SetString(PVPDUEL_ATTRIBUTE, "");
                PvpTree.SetString(PVPBATTLE_ATTRIBUTE, "");
                entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
            }
        }
        public bool GetMessages
        {
            get => PvpTree.GetBool(MSG_ATTRIBUTE, false);
            set
            {
                PvpTree.SetBool(MSG_ATTRIBUTE, value);
                entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);
            }
        }

        public void MarkDirty() => entity.WatchedAttributes.MarkPathDirty(PVPFLAGTREE_ATTRIBUTE_TREE_NAME);

        public override string PropertyName() => "PVPFlag";
        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            if (entity.WatchedAttributes.GetTreeAttribute(PVPFLAGTREE_ATTRIBUTE_TREE_NAME) == null)
                entity.WatchedAttributes.SetAttribute(PVPFLAGTREE_ATTRIBUTE_TREE_NAME, new TreeAttribute());
            MarkDirty();
        }
        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            DueledEntityUID = "";
            base.OnEntityDeath(damageSourceForDeath);
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (entity.Api.Side == EnumAppSide.Client)
                return;


            if (damageSource.CauseEntity is not EntityPlayer && damageSource.SourceEntity is not EntityPlayer)
            {
                base.OnEntityReceiveDamage(damageSource, ref damage);
                return; //Attacker is not a pvp entity
            }


            var attacker = (damageSource.CauseEntity is EntityPlayer ? damageSource.CauseEntity : damageSource.SourceEntity) as EntityPlayer;
            if (entity == attacker)
            {
                base.OnEntityReceiveDamage(damageSource, ref damage);
                return; //Attacking yourself
            }

            var attackerpvp = attacker.GetBehavior<EntityBehaviorPvp>();
            if (attackerpvp.FlagOfWar && FlagOfWar)
            {
                base.OnEntityReceiveDamage(damageSource, ref damage);
                return; //Flag of War
            }
            else if (BattleCode != "" && attackerpvp.BattleCode == BattleCode)
            {
                base.OnEntityReceiveDamage(damageSource, ref damage);
                return; //Battle
            }
            else if (attackerpvp.DueledEntityUID == ((EntityPlayer)entity).PlayerUID && DueledEntityUID == attacker.PlayerUID)
            {
                base.OnEntityReceiveDamage(damageSource, ref damage);
                return; //Duel
            }
            else
            {
                damage = 0;
                damageSource.KnockbackStrength = 0;
                damageSource.DamageTier = 0;
                damageSource.Type = EnumDamageType.Heal;
                if (attackerpvp.GetMessages && entity.Api is ICoreServerAPI api && attacker is IServerPlayer isp)
                    isp.SendMessage(GlobalConstants.InfoLogChatGroup, $"{entity.GetName()} has disabled pvp! /pvp msg to disable this message", EnumChatType.OwnMessage, null);
            }

        }
    }
}
