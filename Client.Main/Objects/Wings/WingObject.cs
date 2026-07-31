using Client.Main.Content;
using Client.Main.Core.Utilities;
using Client.Main.Models;
using Client.Main.Objects.Effects;
using Client.Data.BMD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Client.Main.Controls.UI.Game.Inventory;

namespace Client.Main.Objects.Wings
{
    public class CustomEffect
    {
        public int BoneID { get; set; }
        public EffectType EffectID { get; set; }
        public Vector3 Angle { get; set; }
        public Vector3 Position { get; set; }
        public float Scale { get; set; }
        public Vector3 Color { get; set; }
        public SpriteObject Effect { get; set; }
    }

    public class WingObject : ModelObject
    {
        public List<CustomEffect> _effects { get; set; } = new List<CustomEffect>();

        private const int DefaultWingBoneLink = 47;
        private const int CapeBoneLink = 19;
        private const float CapeRigidAnimationSpeed = 0.25f * 25f;

        private readonly CapeClothObject _capeCloth;
        private bool _isCapeLike;
        private bool _savedBaseState;
        private Vector3 _savedPosition;
        private Vector3 _savedAngle;
        private int _savedParentBoneLink;
        private bool _savedLinkParentAnimation;
        private int _savedHiddenMesh;
        private float _savedAnimationSpeed;
        private bool _savedContinuousAnimation;
        private bool _savedFreezeAnimationPose;
        private int _changeVersion;

        private short _type;
        public new short Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    int changeVersion = Interlocked.Increment(ref _changeVersion);
                    _ = OnChangeType(_type, changeVersion);
                }
            }
        }

        private short itemIndex = -1;
        public short ItemIndex
        {
            get => itemIndex;
            set
            {
                if (itemIndex == value)
                    return;

                itemIndex = value;
                int changeVersion = Interlocked.Increment(ref _changeVersion);
                _ = OnChangeIndex(itemIndex, changeVersion);
            }
        }

        public WingObject()
        {
            RenderShadow = true;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.AlphaBlend;
            BlendMesh = -1;
            BlendMeshState = BlendState.Additive;
            Alpha = 1f;
            LinkParentAnimation = false;
            ParentBoneLink = DefaultWingBoneLink;

            _capeCloth = new CapeClothObject();
            Children.Add(_capeCloth);
        }

        internal bool IsCapeLike => _isCapeLike;

        protected override bool ForceTwoSidedMeshes => _isCapeLike;
        protected override bool AllowGpuSkinning => !_isCapeLike;
        protected override bool AllowDynamicLightingShader => !_isCapeLike;
        protected override bool PreserveBlendMeshesInLowQuality => _isCapeLike;

        protected override float GetDepthBias()
        {
            return -0.000012f;
        }

        private bool IsCurrentChangeVersion(int changeVersion) =>
            Volatile.Read(ref _changeVersion) == changeVersion;

        private void UpdateStatusAfterAsyncResolve(bool modelResolved)
        {
            if (Status is not (GameControlStatus.Ready or GameControlStatus.Error))
                return;

            Status = modelResolved ? GameControlStatus.Ready : GameControlStatus.Error;
        }

        private async Task OnChangeType(short requestedType, int changeVersion)
        {
            if (!IsCurrentChangeVersion(changeVersion))
                return;

            if (requestedType <= 0)
            {
                if (ItemIndex < 0)
                {
                    ApplyWingState();
                    Model = null;
                }
                return;
            }

            string modelPath = Path.Combine("Item", $"Wing{requestedType:D2}.bmd");
            BMD resolvedModel = await BMDLoader.Instance.Prepare(modelPath);

            if (resolvedModel == null)
            {
                modelPath = Path.Combine("Item", $"Wing{requestedType}.bmd");
                resolvedModel = await BMDLoader.Instance.Prepare(modelPath);
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            ApplyWingState();
            Model = resolvedModel;
            UpdateStatusAfterAsyncResolve(resolvedModel != null);
        }

        private async Task OnChangeIndex(short requestedItemIndex, int changeVersion)
        {
            if (!IsCurrentChangeVersion(changeVersion))
                return;

            if (requestedItemIndex < 0)
            {
                if (Type <= 0)
                {
                    ApplyWingState();
                    Model = null;
                }
                return;
            }

            ItemDefinition itemDefinition = ItemDatabase.GetItemDefinition(12, requestedItemIndex);
            string modelPath = itemDefinition?.TexturePath;
            BMD resolvedModel = null;

            if (string.IsNullOrWhiteSpace(modelPath))
            {
                resolvedModel = requestedItemIndex switch
                {
                    30 or 130 => await BMDLoader.Instance.Prepare("Item/DarkLordRobe.bmd")
                                 ?? await BMDLoader.Instance.Prepare("Item/DarkLordRobe02.bmd"),
                    40 => await BMDLoader.Instance.Prepare("Item/DarkLordRobe02.bmd"),
                    49 or 135 => await BMDLoader.Instance.Prepare("Item/Wing50.bmd"),
                    50 => await BMDLoader.Instance.Prepare("Item/Wing51.bmd"),
                    _ => null
                };
            }
            else
            {
                string normalized = NormalizeItemModelPath(modelPath);
                modelPath = normalized;
                resolvedModel = await BMDLoader.Instance.Prepare(normalized);

                if (resolvedModel == null && normalized.EndsWith("DarkLordRobe.bmd", StringComparison.OrdinalIgnoreCase))
                {
                    string robe02Path = normalized.Replace("DarkLordRobe.bmd", "DarkLordRobe02.bmd");
                    resolvedModel = await BMDLoader.Instance.Prepare(robe02Path);
                }
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            if (CapeClothProfile.TryCreate(requestedItemIndex, modelPath, out CapeClothProfile capeProfile) &&
                resolvedModel != null)
            {
                Model = resolvedModel;
                bool clothReady = await _capeCloth.ConfigureAsync(capeProfile, resolvedModel);

                if (!IsCurrentChangeVersion(changeVersion))
                    return;

                ApplyCapeState(capeProfile, clothReady);
            }
            else
            {
                ApplyWingState();
                Model = resolvedModel;
            }

            if (!IsCurrentChangeVersion(changeVersion))
                return;

            UpdateStatusAfterAsyncResolve(resolvedModel != null);
        }

        private static string NormalizeItemModelPath(string modelPath)
        {
            string normalized = modelPath.Replace("\\", "/");
            if (normalized.Contains("Item/Wing/", StringComparison.OrdinalIgnoreCase))
                normalized = Path.Combine("Item", Path.GetFileName(normalized)).Replace("\\", "/");
            if (normalized.Contains("DarkLordRobe01", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Replace("DarkLordRobe01", "DarkLordRobe");
            return normalized;
        }

        private void SaveBaseState()
        {
            if (_savedBaseState)
                return;

            _savedPosition = Position;
            _savedAngle = Angle;
            _savedParentBoneLink = ParentBoneLink;
            _savedLinkParentAnimation = LinkParentAnimation;
            _savedHiddenMesh = HiddenMesh;
            _savedAnimationSpeed = AnimationSpeed;
            _savedContinuousAnimation = ContinuousAnimation;
            _savedFreezeAnimationPose = FreezeAnimationPose;
            _savedBaseState = true;
        }

        private void ApplyCapeState(CapeClothProfile profile, bool clothReady)
        {
            SaveBaseState();
            _isCapeLike = true;
            LinkParentAnimation = false;
            ParentBoneLink = CapeBoneLink;
            HiddenMesh = profile.RigidAttachment.Visible || !clothReady ? -1 : -2;
            Position = profile.RigidAttachment.Position;
            Angle = profile.RigidAttachment.Angle;
            AnimationSpeed = CapeRigidAnimationSpeed;
            ContinuousAnimation = true;
            FreezeAnimationPose = false;
            BlendState = BlendState.AlphaBlend;
            BlendMeshState = BlendState.Additive;
            RenderShadow = profile.RigidAttachment.Visible || !clothReady;
            InvalidateBuffers(BufferFlagAll);
        }

        private void ApplyWingState()
        {
            _capeCloth.Disable();

            if (_savedBaseState)
            {
                Position = _savedPosition;
                Angle = _savedAngle;
                ParentBoneLink = _savedParentBoneLink;
                LinkParentAnimation = _savedLinkParentAnimation;
                HiddenMesh = _savedHiddenMesh;
                AnimationSpeed = _savedAnimationSpeed;
                ContinuousAnimation = _savedContinuousAnimation;
                FreezeAnimationPose = _savedFreezeAnimationPose;
                _savedBaseState = false;
            }
            else
            {
                ParentBoneLink = DefaultWingBoneLink;
                HiddenMesh = -1;
                FreezeAnimationPose = false;
            }

            RenderShadow = true;
            _isCapeLike = false;
            InvalidateBuffers(BufferFlagAll);
        }


        internal void UpdateCapePhysics(GameTime gameTime)
        {
            if (_isCapeLike)
                _capeCloth.UpdateAfterOwner(gameTime);
        }

        public override async Task Load()
        {
            if (_effects.Count > 0)
            {
                foreach (CustomEffect effect in _effects)
                {
                    effect.Effect = Utils.GetEffectByCode(effect.EffectID);
                    effect.Effect.Light = effect.Color;
                    effect.Effect.Scale = effect.Scale;
                    Children.Add(effect.Effect);
                }
            }

            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_effects.Count > 0 && BoneTransform != null)
            {
                foreach (CustomEffect effect in _effects)
                {
                    if ((uint)effect.BoneID >= (uint)BoneTransform.Length)
                        continue;
                    effect.Effect.Position = effect.Position + BoneTransform[effect.BoneID].Translation;
                    effect.Effect.Angle = effect.Angle + Angle;
                }
            }
        }
    }
}
