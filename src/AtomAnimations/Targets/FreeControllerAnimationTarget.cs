using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VamTimeline
{
    public class FreeControllerAnimationTarget : AnimationTargetBase, IAnimationTargetWithCurves
    {
        public readonly FreeControllerV3 controller;
        public readonly SortedDictionary<int, KeyframeSettings> settings = new SortedDictionary<int, KeyframeSettings>();
        public readonly AnimationCurve x = new AnimationCurve();
        public readonly AnimationCurve y = new AnimationCurve();
        public readonly AnimationCurve z = new AnimationCurve();
        public readonly AnimationCurve rotX = new AnimationCurve();
        public readonly AnimationCurve rotY = new AnimationCurve();
        public readonly AnimationCurve rotZ = new AnimationCurve();
        public readonly AnimationCurve rotW = new AnimationCurve();
        public readonly List<AnimationCurve> curves;

        public string name => controller.name;

        public FreeControllerAnimationTarget(FreeControllerV3 controller)
        {
            curves = new List<AnimationCurve> {
                x, y, z, rotX, rotY, rotZ, rotW
            };
            this.controller = controller;
        }

        public string GetShortName()
        {
            if (name.EndsWith("Control"))
                return name.Substring(0, name.Length - "Control".Length);
            return name;
        }

        public void Sample(float clipTime, float weight)
        {
            if (controller?.control == null) return;
            var control = controller.control;

            var rotState = controller.currentRotationState;
            if (rotState == FreeControllerV3.RotationState.On)
            {
                var localRotation = Quaternion.Slerp(control.localRotation, EvaluateRotation(clipTime), weight);
                control.localRotation = localRotation;
                // control.rotation = controller.linkToRB.rotation * localRotation;
            }

            var posState = controller.currentPositionState;
            if (posState == FreeControllerV3.PositionState.On)
            {
                var localPosition = Vector3.Lerp(control.localPosition, EvaluatePosition(clipTime), weight);
                control.localPosition = localPosition;
                // control.position = controller.linkToRB.position + Vector3.Scale(localPosition, control.transform.localScale);
            }
        }

        #region Control

        public AnimationCurve GetLeadCurve()
        {
            return x;
        }

        public IEnumerable<AnimationCurve> GetCurves()
        {
            return curves;
        }

        public void Validate(float animationLength)
        {
            var leadCurve = GetLeadCurve();
            if (leadCurve.length < 2)
            {
                SuperController.LogError($"Target {name} has {leadCurve.length} frames");
                return;
            }
            if (x[0].time != 0)
            {
                SuperController.LogError($"Target {name} has no start frame");
                return;
            }
            if (x[x.length - 1].time != animationLength)
            {
                SuperController.LogError($"Target {name} ends with frame {x[x.length - 1].time} instead of expected {animationLength}");
                return;
            }
            if (this.settings.Count > leadCurve.length)
            {
                var curveKeys = leadCurve.keys.Select(k => k.time.ToMilliseconds()).ToList();
                var extraneousKeys = this.settings.Keys.Except(curveKeys);
                SuperController.LogError($"Target {name} has {leadCurve.length} frames but {this.settings.Count} settings. Attempting auto-repair.");
                foreach (var extraneousKey in extraneousKeys)
                    this.settings.Remove(extraneousKey);
            }
            if (this.settings.Count != leadCurve.length)
            {
                SuperController.LogError($"Target {name} has {leadCurve.length} frames but {this.settings.Count} settings");
                SuperController.LogError($"  Target  : {string.Join(", ", leadCurve.keys.Select(k => k.time.ToString()).ToArray())}");
                SuperController.LogError($"  Settings: {string.Join(", ", this.settings.Select(k => (k.Key / 1000f).ToString()).ToArray())}");
                return;
            }
            var settings = this.settings.Select(s => s.Key);
            var keys = leadCurve.keys.Select(k => k.time.ToMilliseconds()).ToArray();
            if (!settings.SequenceEqual(keys))
            {
                SuperController.LogError($"Target {name} has different times for settings and keyframes");
                SuperController.LogError($"Settings: {string.Join(", ", settings.Select(s => s.ToString()).ToArray())}");
                SuperController.LogError($"Keyframes: {string.Join(", ", keys.Select(k => k.ToString()).ToArray())}");
                return;
            }
        }

        public void ReapplyCurveTypes(bool loop)
        {
            if (x.length < 2) return;

            foreach (var curve in curves)
            {
                for (var key = 0; key < curve.length; key++)
                {
                    KeyframeSettings setting;
                    if (!settings.TryGetValue(curve[key].time.ToMilliseconds(), out setting)) continue;
                    curve.ApplyCurveType(key, setting.curveType, loop);
                }
            }
        }

        public void SmoothLoop()
        {
            foreach (var curve in curves)
            {
                curve.SmoothLoop();
            }
        }

        #endregion

        #region Keyframes control

        public int SetKeyframeToCurrentTransform(float time)
        {
            return SetKeyframe(time, controller.transform.localPosition, controller.transform.localRotation);
        }

        public int SetKeyframe(float time, Vector3 localPosition, Quaternion locationRotation)
        {
            var key = x.SetKeyframe(time, localPosition.x);
            y.SetKeyframe(time, localPosition.y);
            z.SetKeyframe(time, localPosition.z);
            rotX.SetKeyframe(time, locationRotation.x);
            rotY.SetKeyframe(time, locationRotation.y);
            rotZ.SetKeyframe(time, locationRotation.z);
            rotW.SetKeyframe(time, locationRotation.w);
            var ms = time.ToMilliseconds();
            if (!settings.ContainsKey(ms))
                settings[ms] = new KeyframeSettings { curveType = CurveTypeValues.Smooth };
            dirty = true;
            return key;
        }

        public void DeleteFrame(float time)
        {
            var key = GetLeadCurve().KeyframeBinarySearch(time);
            if (key != -1) DeleteFrameByKey(key);
        }

        public void DeleteFrameByKey(int key)
        {
            var settingIndex = settings.Remove(GetLeadCurve()[key].time.ToMilliseconds());
            foreach (var curve in curves)
            {
                curve.RemoveKey(key);
            }
            dirty = true;
        }

        public void AddEdgeFramesIfMissing(float animationLength)
        {
            x.AddEdgeFramesIfMissing(animationLength);
            y.AddEdgeFramesIfMissing(animationLength);
            z.AddEdgeFramesIfMissing(animationLength);
            rotX.AddEdgeFramesIfMissing(animationLength);
            rotY.AddEdgeFramesIfMissing(animationLength);
            rotZ.AddEdgeFramesIfMissing(animationLength);
            rotW.AddEdgeFramesIfMissing(animationLength);
            if (!settings.ContainsKey(0))
                settings.Add(0, new KeyframeSettings { curveType = CurveTypeValues.Smooth });
            if (!settings.ContainsKey(animationLength.ToMilliseconds()))
                settings.Add(animationLength.ToMilliseconds(), new KeyframeSettings { curveType = CurveTypeValues.Smooth });
            dirty = true;
        }

        public float[] GetAllKeyframesTime()
        {
            var curve = x;
            var keyframes = new float[curve.length];
            for (var i = 0; i < curve.length; i++)
                keyframes[i] = curve[i].time;
            return keyframes;
        }

        public float GetTimeClosestTo(float time)
        {
            return x[x.KeyframeBinarySearch(time, true)].time;
        }

        public bool HasKeyframe(float time)
        {
            return x.KeyframeBinarySearch(time) != -1;
        }

        #endregion

        #region Curves

        public void ChangeCurve(float time, string curveType)
        {
            if (string.IsNullOrEmpty(curveType)) return;

            UpdateSetting(time, curveType, false);
            dirty = true;
        }

        #endregion

        #region Evaluate

        public Vector3 EvaluatePosition(float time)
        {
            return new Vector3(
                x.Evaluate(time),
                y.Evaluate(time),
                z.Evaluate(time)
            );
        }

        public Quaternion EvaluateRotation(float time)
        {
            return new Quaternion(
                rotX.Evaluate(time),
                rotY.Evaluate(time),
                rotZ.Evaluate(time),
                rotW.Evaluate(time)
            );
        }

        #endregion

        #region Snapshots

        public FreeControllerV3Snapshot GetCurveSnapshot(float time)
        {
            if (x.KeyframeBinarySearch(time) == -1) return null;
            KeyframeSettings setting;
            return new FreeControllerV3Snapshot
            {
                x = x[x.KeyframeBinarySearch(time)],
                y = y[y.KeyframeBinarySearch(time)],
                z = z[z.KeyframeBinarySearch(time)],
                rotX = rotX[rotX.KeyframeBinarySearch(time)],
                rotY = rotY[rotY.KeyframeBinarySearch(time)],
                rotZ = rotZ[rotZ.KeyframeBinarySearch(time)],
                rotW = rotW[rotW.KeyframeBinarySearch(time)],
                curveType = settings.TryGetValue(time.ToMilliseconds(), out setting) ? setting.curveType : CurveTypeValues.LeaveAsIs
            };
        }

        public void SetCurveSnapshot(float time, FreeControllerV3Snapshot snapshot, bool dirty = true)
        {
            x.SetKeySnapshot(time, snapshot.x);
            y.SetKeySnapshot(time, snapshot.y);
            z.SetKeySnapshot(time, snapshot.z);
            rotX.SetKeySnapshot(time, snapshot.rotX);
            rotY.SetKeySnapshot(time, snapshot.rotY);
            rotZ.SetKeySnapshot(time, snapshot.rotZ);
            rotW.SetKeySnapshot(time, snapshot.rotW);
            UpdateSetting(time, snapshot.curveType, true);
            if (dirty) base.dirty = true;
        }

        private void UpdateSetting(float time, string curveType, bool create)
        {
            var ms = time.ToMilliseconds();
            if (settings.ContainsKey(ms))
                settings[ms].curveType = curveType;
            else if (create)
                settings.Add(ms, new KeyframeSettings { curveType = curveType });
        }

        #endregion

        #region Interpolation

        public bool Interpolate(float clipTime, float maxDistanceDelta, float maxRadiansDelta)
        {
            var targetLocalPosition = new Vector3
            {
                x = x.Evaluate(clipTime),
                y = y.Evaluate(clipTime),
                z = z.Evaluate(clipTime)
            };

            var targetLocalRotation = new Quaternion
            {
                x = rotX.Evaluate(clipTime),
                y = rotY.Evaluate(clipTime),
                z = rotZ.Evaluate(clipTime),
                w = rotW.Evaluate(clipTime)
            };

            controller.transform.localPosition = Vector3.MoveTowards(controller.transform.localPosition, targetLocalPosition, maxDistanceDelta);
            controller.transform.localRotation = Quaternion.RotateTowards(controller.transform.localRotation, targetLocalRotation, maxRadiansDelta);

            var posDistance = Vector3.Distance(controller.transform.localPosition, targetLocalPosition);
            // NOTE: We skip checking for rotation reached because in some cases we just never get even near the target rotation.
            // var rotDistance = Quaternion.Dot(Controller.transform.localRotation, targetLocalRotation);
            return posDistance < 0.01f;
        }

        #endregion

        public bool TargetsSameAs(IAtomAnimationTarget target)
        {
            var t = target as FreeControllerAnimationTarget;
            if (t == null) return false;
            return t.controller == controller;
        }

        public class Comparer : IComparer<FreeControllerAnimationTarget>
        {
            public int Compare(FreeControllerAnimationTarget t1, FreeControllerAnimationTarget t2)
            {
                return t1.controller.name.CompareTo(t2.controller.name);

            }
        }

        public void SmoothNeighbors(int key)
        {
            if (key == -1) return;
            x.SmoothTangents(key, 1f);
            if (key > 0) x.SmoothTangents(key - 1, 1f);
            if (key < x.length - 1) x.SmoothTangents(key + 1, 1f);

            y.SmoothTangents(key, 1f);
            if (key > 0) y.SmoothTangents(key - 1, 1f);
            if (key < y.length - 1) y.SmoothTangents(key + 1, 1f);

            z.SmoothTangents(key, 1f);
            if (key > 0) z.SmoothTangents(key - 1, 1f);
            if (key < z.length - 1) z.SmoothTangents(key + 1, 1f);
        }
    }
}
