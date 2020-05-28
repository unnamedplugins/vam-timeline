using System.Collections.Generic;
using System.Linq;

namespace VamTimeline
{
    /// <summary>
    /// VaM Timeline
    /// By Acidbubbles
    /// Animation timeline with keyframes
    /// Source: https://github.com/acidbubbles/vam-timeline
    /// </summary>
    public class AtomAnimationControllersUI : AtomAnimationBaseUI
    {
        public const string ScreenName = "Controllers";
        public override string Name => ScreenName;

        private class TargetRef
        {
            public JSONStorableBool KeyframeJSON;
            public FreeControllerAnimationTarget Target;
            public UIDynamicToggle KeyframeUI;
        }

        private readonly List<TargetRef> _targets = new List<TargetRef>();
        private JSONStorableStringChooser _curveTypeJSON;
        private Curves _curves;

        public AtomAnimationControllersUI(IAtomPlugin plugin)
            : base(plugin)
        {

        }
        public override void Init()
        {
            base.Init();

            // Left side

            InitChangeCurveTypeUI(false);

            InitClipboardUI(false);

            InitAutoKeyframeUI();

            // Right side

            InitCurvesUI(true);
        }

        private void InitChangeCurveTypeUI(bool rightSide)
        {
            _curveTypeJSON = new JSONStorableStringChooser(StorableNames.ChangeCurve, CurveTypeValues.DisplayCurveTypes, "", "Change Curve", ChangeCurve);
            RegisterStorable(_curveTypeJSON);
            var curveTypeUI = Plugin.CreateScrollablePopup(_curveTypeJSON, rightSide);
            curveTypeUI.popupPanelHeight = 340f;
            RegisterComponent(curveTypeUI);
        }

        private void InitAutoKeyframeUI()
        {
            RegisterStorable(Plugin.AutoKeyframeAllControllersJSON);
            var autoKeyframeAllControllersUI = Plugin.CreateToggle(Plugin.AutoKeyframeAllControllersJSON, false);
            RegisterComponent(autoKeyframeAllControllersUI);
        }

        private void InitCurvesUI(bool rightSide)
        {
            var spacerUI = Plugin.CreateSpacer(rightSide);
            spacerUI.height = 320f;
            RegisterComponent(spacerUI);

            _curves = spacerUI.gameObject.AddComponent<Curves>();
            UpdateCurves();

            // TODO: When changing the current clip this won't update
            Current.SelectedChanged.AddListener(UpdateCurves);
        }

        private void UpdateCurves()
        {
            if (_curves == null) return;
            var targets = Current.GetAllOrSelectedControllerTargets();
            if (targets.Count == 1)
                _curves.Bind(targets[0]);
            else
                _curves.Bind(null);
            _curves?.SetScrubberPosition(Plugin.Animation.Time);
        }

        public override void UpdatePlaying()
        {
            base.UpdatePlaying();
            UpdateValues();
            _curves?.SetScrubberPosition(Plugin.Animation.Time);
        }

        public override void AnimationFrameUpdated()
        {
            base.AnimationFrameUpdated();
            UpdateValues();
            UpdateCurrentCurveType();
            _curves?.SetScrubberPosition(Plugin.Animation.Time);
        }

        public override void AnimationModified()
        {
            base.AnimationModified();
            RefreshTargetsList();
            UpdateCurrentCurveType();
            UpdateCurves();
        }

        protected override void AnimationChanged(AtomAnimationClip before, AtomAnimationClip after)
        {
            before.SelectedChanged.RemoveListener(UpdateCurves);
            after.SelectedChanged.AddListener(UpdateCurves);
        }

        private void UpdateCurrentCurveType()
        {
            if (_curveTypeJSON == null) return;

            var time = Plugin.Animation.Time.Snap();
            if (Current.Loop && (time.IsSameFrame(0) || time.IsSameFrame(Current.AnimationLength)))
            {
                _curveTypeJSON.valNoCallback = "(Loop)";
                return;
            }
            var ms = time.ToMilliseconds();
            var curveTypes = new HashSet<string>();
            foreach (var target in Current.GetAllOrSelectedControllerTargets())
            {
                KeyframeSettings v;
                if (!target.Settings.TryGetValue(ms, out v)) continue;
                curveTypes.Add(v.CurveType);
            }
            if (curveTypes.Count == 0)
                _curveTypeJSON.valNoCallback = "(No Keyframe)";
            else if (curveTypes.Count == 1)
                _curveTypeJSON.valNoCallback = curveTypes.First().ToString();
            else
                _curveTypeJSON.valNoCallback = "(" + string.Join("/", curveTypes.ToArray()) + ")";
        }

        private void UpdateValues()
        {
            var time = Plugin.Animation.Time;
            foreach (var targetRef in _targets)
            {
                targetRef.KeyframeJSON.valNoCallback = targetRef.Target.GetLeadCurve().KeyframeBinarySearch(time) != -1;
            }
        }

        private void RefreshTargetsList()
        {
            if (Plugin.Animation == null) return;
            if (Enumerable.SequenceEqual(Current.TargetControllers, _targets.Select(t => t.Target)))
            {
                UpdateValues();
                return;
            }
            RemoveTargets();
            var time = Plugin.Animation.Time;
            foreach (var target in Current.TargetControllers)
            {
                var keyframeJSON = new JSONStorableBool($"{target.Name} Keyframe", target.GetLeadCurve().KeyframeBinarySearch(time) != -1, (bool val) => ToggleKeyframe(target, val));
                var keyframeUI = Plugin.CreateToggle(keyframeJSON, true);
                _targets.Add(new TargetRef
                {
                    Target = target,
                    KeyframeJSON = keyframeJSON,
                    KeyframeUI = keyframeUI
                });
            }
        }

        public override void Dispose()
        {
            RemoveTargets();
            base.Dispose();
        }

        private void RemoveTargets()
        {
            foreach (var targetRef in _targets)
            {
                Plugin.RemoveToggle(targetRef.KeyframeJSON);
                Plugin.RemoveToggle(targetRef.KeyframeUI);
            }
            _targets.Clear();
        }

        private void ChangeCurve(string curveType)
        {
            if (string.IsNullOrEmpty(curveType) || curveType.StartsWith("("))
            {
                UpdateCurrentCurveType();
                return;
            }
            float time = Plugin.Animation.Time.Snap();
            if (time.IsSameFrame(0) && curveType == CurveTypeValues.CopyPrevious)
            {
                UpdateCurrentCurveType();
                return;
            }
            if (Plugin.Animation.IsPlaying()) return;
            if (Current.Loop && (time.IsSameFrame(0) || time.IsSameFrame(Current.AnimationLength)))
            {
                UpdateCurrentCurveType();
                return;
            }
            Current.ChangeCurve(time, curveType);
            Plugin.Animation.RebuildAnimation();
            Plugin.AnimationModified();
        }

        private void ToggleKeyframe(FreeControllerAnimationTarget target, bool enable)
        {
            if (Plugin.Animation.IsPlaying()) return;
            var time = Plugin.Animation.Time.Snap();
            if (time.IsSameFrame(0f) || time.IsSameFrame(Current.AnimationLength))
            {
                _targets.First(t => t.Target == target).KeyframeJSON.valNoCallback = true;
                return;
            }
            if (enable)
            {
                if (Plugin.AutoKeyframeAllControllersJSON.val)
                {
                    foreach (var target1 in Current.TargetControllers)
                        SetControllerKeyframe(time, target1);
                }
                else
                {
                    SetControllerKeyframe(time, target);
                }
            }
            else
            {
                if (Plugin.AutoKeyframeAllControllersJSON.val)
                {
                    foreach (var target1 in Current.TargetControllers)
                        target1.DeleteFrame(time);
                }
                else
                {
                    target.DeleteFrame(time);
                }
            }
            Plugin.Animation.RebuildAnimation();
            Plugin.AnimationModified();
        }

        private void SetControllerKeyframe(float time, FreeControllerAnimationTarget target)
        {
            Plugin.Animation.SetKeyframeToCurrentTransform(target, time);
            if (target.Settings[time.ToMilliseconds()]?.CurveType == CurveTypeValues.CopyPrevious)
                Current.ChangeCurve(time, CurveTypeValues.Smooth);
        }
    }
}

