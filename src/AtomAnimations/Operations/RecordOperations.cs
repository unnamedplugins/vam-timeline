﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VamTimeline
{
    public class RecordOperations
    {
        private readonly AtomAnimation _animation;
        private readonly AtomAnimationClip _clip;

        public RecordOperations(AtomAnimation animation, AtomAnimationClip clip)
        {
            _animation = animation;
            _clip = clip;
        }

        public IEnumerator StartRecording(List<FreeControllerAnimationTarget> controllers, List<FloatParamAnimationTarget> floatParams)
        {
            // TODO: Handle stopping in the middle of it
            // TODO: Handle starting while it's already recording
            // TODO: Counter should use in-game rather than helptext

            _animation.StopAll();
            _animation.ResetAll();

            var keyframesOps = new KeyframesOperations(_clip);

            for (var i = 5; i > 0; i--)
            {
                SuperController.singleton.helpText = $"Start recording in {i}...";
                var next = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < next)
                {
                    yield return 0;
                    if (Input.GetKeyDown(KeyCode.Escape))
                        yield break;
                }
            }

            SuperController.singleton.helpText = "Recording...";

            foreach (var target in controllers)
            {
                keyframesOps.RemoveAll(target);
            }
            foreach (var target in floatParams)
            {
                keyframesOps.RemoveAll(target);
            }

            _clip.DirtyAll();
            _animation.RebuildAnimationNow();

            yield return 0;

            foreach (var target in controllers)
            {
                target.recording = true;
                target.StartBulkUpdates();
            }
            foreach (var target in floatParams)
            {
                target.recording = true;
                target.StartBulkUpdates();
            }

            _animation.PlayClip(_clip, false);

            while (_animation.playTime <= _clip.animationLength && _animation.isPlaying)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                    break;
                yield return 0;
            }

            _animation.StopAll();
            _animation.ResetAll();
            SuperController.singleton.helpText = "";

            // TODO: This needs to be guaranteed. We could register the enumerator inside a disposable class, dispose being called in different cancel situations
            foreach (var target in controllers)
            {
                target.recording = false;
                target.EndBulkUpdates();
            }
            foreach (var target in floatParams)
            {
                target.recording = false;
                target.EndBulkUpdates();
            }

            _clip.DirtyAll();
        }
    }
}
