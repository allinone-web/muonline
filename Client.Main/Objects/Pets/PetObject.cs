#nullable enable
using System;
using Client.Main.Content;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Pets
{
    /// <summary>
    /// Pet action types matching SourceMain PetObject::ActionType.
    /// </summary>
    public enum PetActionType
    {
        Stand = 0,
        Move = 1,
        Attack = 2,
        Skill = 3,
        Birth = 4,
        Dead = 5,
        Round = 6,
        Collect = 7,
    }

    /// <summary>
    /// Pet command types matching SourceMain pet commands.
    /// </summary>
    public enum PetCommand
    {
        None = 0,
        AttackRandom = 1,
        AttackSameTarget = 2,
        AttackWithOwner = 3,
        Defense = 4,
        Collect = 5,
        Wait = 6,
    }

    /// <summary>
    /// Base pet object rendered as a BMD model following the owner.
    /// Matches SourceMain PetObject: follows owner, switches actions based on AI state.
    /// </summary>
    public class PetObject : ModelObject
    {
        protected const int AnimStand = 0;
        protected const int AnimMove = 1;
        protected const int AnimAttack = 2;
        protected const int AnimSkill = 3;
        protected const int AnimBirth = 4;
        protected const int AnimDead = 5;

        protected PlayerObject? _owner;
        protected PetActionType _currentAction = PetActionType.Stand;
        protected PetCommand _command = PetCommand.AttackRandom;
        protected int _targetKey = -1;

        protected float _actionSpeed = 1.0f;
        protected float _petScale = 1.0f;

        /// <summary>Distance from owner at which the pet starts following (in tile units).</summary>
        protected const float FollowDistance = 2.5f;

        /// <summary>Distance from target at which the pet can attack.</summary>
        protected const float AttackDistance = 2.0f;

        public PetObject()
        {
            RenderShadow = true;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.AlphaBlend;
            BlendMesh = -1;
            BlendMeshState = BlendState.Additive;
            Alpha = 1f;
            LinkParentAnimation = false;
            AnimationSpeed = 25f;
        }

        public void SetOwner(PlayerObject owner) => _owner = owner;
        public void SetCommand(PetCommand command) => _command = command;

        public void SetScale(float scale)
        {
            _petScale = scale;
            Scale = scale;
        }

        public void SetTarget(int targetKey) => _targetKey = targetKey;

        /// <summary>Gets 2D tile-position from the model's world position.</summary>
        private Vector2 TilePosition => new(WorldPosition.Translation.X, WorldPosition.Translation.Y);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_owner == null || _owner.IsDead || Hidden)
                return;

            UpdatePetBehavior(gameTime);
        }

        protected virtual void UpdatePetBehavior(GameTime gameTime)
        {
            if (_owner == null) return;

            float distToOwner = Vector2.Distance(TilePosition, _owner.Location);

            switch (_command)
            {
                case PetCommand.Wait:
                    SetCurrentAction(PetActionType.Stand);
                    break;

                case PetCommand.AttackRandom:
                case PetCommand.AttackSameTarget:
                case PetCommand.AttackWithOwner:
                    if (_targetKey > 0 && distToOwner < 15f)
                    {
                        var target = FindTargetInWorld();
                        if (target != null)
                        {
                            float distToTarget = Vector2.Distance(TilePosition, target.Location);
                            if (distToTarget <= AttackDistance)
                            {
                                SetCurrentAction(PetActionType.Attack);
                            }
                            else
                            {
                                SetCurrentAction(PetActionType.Move);
                                MoveTowards(target.Location);
                            }
                            return;
                        }
                    }
                    FollowOwner(distToOwner);
                    break;

                case PetCommand.Collect:
                case PetCommand.Defense:
                default:
                    FollowOwner(distToOwner);
                    break;
            }
        }

        protected void FollowOwner(float distToOwner)
        {
            if (_owner == null) return;

            if (distToOwner > FollowDistance)
            {
                SetCurrentAction(PetActionType.Move);
                MoveTowards(_owner.Location);
            }
            else
            {
                SetCurrentAction(PetActionType.Stand);
            }
        }

        protected void MoveTowards(Vector2 target)
        {
            var myPos = TilePosition;
            var dir = target - myPos;
            if (dir == Vector2.Zero) return;

            dir.Normalize();
            float speed = 3.0f * _actionSpeed * 0.016f;
            Position = new Vector3(myPos.X + dir.X * speed, myPos.Y + dir.Y * speed, Position.Z);

            float angle = MathF.Atan2(dir.Y, dir.X);
            Angle = new Vector3(0, 0, angle);
        }

        protected WalkerObject? FindTargetInWorld()
        {
            if (_owner?.World == null || _targetKey <= 0)
                return null;

            if (_owner.World is Controls.WalkableWorldControl walkable)
            {
                walkable.WalkerObjectsById.TryGetValue((ushort)_targetKey, out var walker);
                return walker;
            }

            return null;
        }

        protected void SetCurrentAction(PetActionType action)
        {
            if (_currentAction == action) return;
            _currentAction = action;

            int animIndex = action switch
            {
                PetActionType.Stand => AnimStand,
                PetActionType.Move => AnimMove,
                PetActionType.Attack => AnimAttack,
                PetActionType.Skill => AnimSkill,
                PetActionType.Birth => AnimBirth,
                PetActionType.Dead => AnimDead,
                _ => AnimStand
            };

            if (Model != null && animIndex >= 0 && animIndex < Model.Actions.Length)
                CurrentAction = animIndex;
        }

        protected void SetModel(string modelPath, float scale = 1.0f)
        {
            _petScale = scale;
            Scale = scale;
            _ = LoadPetModel(modelPath);
        }

        private async System.Threading.Tasks.Task LoadPetModel(string modelPath)
        {
            var model = await BMDLoader.Instance.Prepare(modelPath);
            if (model != null)
            {
                Model = model;
                Status = GameControlStatus.Ready;
            }
        }
    }
}
