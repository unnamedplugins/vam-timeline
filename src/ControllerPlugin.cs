using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VamTimeline
{
    /// <summary>
    /// VaM Timeline
    /// By Acidbubbles
    /// Animation timeline with keyframes
    /// Source: https://github.com/acidbubbles/vam-timeline
    /// </summary>
    public class ControllerPlugin : MVRScript, IRemoteControllerPlugin
    {
        private const string _atomSeparator = ";";

        private Atom _atom;
        private SimpleSignUI _ui;
        private JSONStorableBool _autoPlayJSON;
        private JSONStorableBool _hideJSON;
        private JSONStorableBool _enableKeyboardShortcuts;
        private JSONStorableBool _lockedJSON;
        private JSONStorableStringChooser _atomsJSON;
        private JSONStorableStringChooser _animationJSON;
        private JSONStorableFloat _timeJSON;
        private JSONStorableAction _playJSON;
        private JSONStorableAction _playIfNotPlayingJSON;
        private JSONStorableAction _stopJSON;
        // TODO: Identify differently
        private SyncProxy _mainLinkedAnimation;
        private UIDynamic _controlPanelSpacer;
        private GameObject _controlPanelContainer;
        private readonly List<SyncProxy> _links = new List<SyncProxy>();
        private bool _ignoreVamTimelineAnimationFrameUpdated;

        #region Initialization

        public override void Init()
        {
            try
            {
                _atom = GetAtom();
                InitStorables();
                InitCustomUI();
                if (!_hideJSON.val)
                    OnEnable();
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline.{nameof(ControllerPlugin)}.{nameof(Init)}: " + exc);
            }
        }

        private Atom GetAtom()
        {
            // Note: Yeah, that's horrible, but containingAtom is null
            var container = gameObject?.transform?.parent?.parent?.parent?.parent?.parent?.gameObject;
            if (container == null)
                throw new NullReferenceException($"Could not find the parent gameObject.");
            var atom = container.GetComponent<Atom>();
            if (atom == null)
                throw new NullReferenceException($"Could not find the parent atom in {container.name}.");
            if (atom.type != "SimpleSign")
                throw new InvalidOperationException("Can only be applied on SimpleSign. This plugin is used to synchronize multiple atoms; use VamTimeline.AtomAnimation.cslist to animate an atom.");
            return atom;
        }

        private void InitStorables()
        {
            _autoPlayJSON = new JSONStorableBool("Auto Play", false);
            RegisterBool(_autoPlayJSON);

            _hideJSON = new JSONStorableBool("Hide", false, (bool val) => Hide(val));
            RegisterBool(_hideJSON);

            _enableKeyboardShortcuts = new JSONStorableBool("Enable Keyboard Shortcuts", false);
            RegisterBool(_enableKeyboardShortcuts);

            _lockedJSON = new JSONStorableBool("Locked (Performance)", false, (bool val) => Lock(val))
            {
                isStorable = false
            };
            RegisterBool(_lockedJSON);

            _atomsJSON = new JSONStorableStringChooser("Atoms Selector", new List<string> { "" }, "", "Atoms", (string v) => SelectCurrentAtom(v))
            {
                isStorable = false
            };

            _animationJSON = new JSONStorableStringChooser("Animation", new List<string>(), "", "Animation", (string v) => ChangeAnimation(v))
            {
                isStorable = false
            };
            RegisterStringChooser(_animationJSON);

            _playJSON = new JSONStorableAction("Play", () => Play());
            RegisterAction(_playJSON);

            _playIfNotPlayingJSON = new JSONStorableAction("Play If Not Playing", () => PlayIfNotPlaying());
            RegisterAction(_playIfNotPlayingJSON);

            _stopJSON = new JSONStorableAction("Stop", () => Stop());
            RegisterAction(_stopJSON);

            _timeJSON = new JSONStorableFloat("Time", 0f, v => _mainLinkedAnimation.time.val = v, 0f, 2f, true)
            {
                isStorable = false
            };
            RegisterFloat(_timeJSON);

            var nextAnimationJSON = new JSONStorableAction(StorableNames.NextAnimation, () =>
            {
                var i = _animationJSON.choices.IndexOf(_mainLinkedAnimation?.animation.val);
                if (i < 0 || i > _animationJSON.choices.Count - 2) return;
                _animationJSON.val = _animationJSON.choices[i + 1];
            });
            RegisterAction(nextAnimationJSON);

            var previousAnimationJSON = new JSONStorableAction(StorableNames.PreviousAnimation, () =>
            {
                var i = _animationJSON.choices.IndexOf(_mainLinkedAnimation?.animation.val);
                if (i < 1 || i > _animationJSON.choices.Count - 1) return;
                _animationJSON.val = _animationJSON.choices[i - 1];
            });
            RegisterAction(previousAnimationJSON);

            StartCoroutine(InitDeferred());
        }

        private IEnumerator InitDeferred()
        {
            if (_hideJSON.val)
                OnDisable();

            while (SuperController.singleton.isLoading)
                yield return 0;

            yield return 0;

            ScanForAtoms();

            while (SuperController.singleton.freezeAnimation)
                yield return 0;

            yield return 0;

            if (_autoPlayJSON.val && _mainLinkedAnimation != null)
                PlayIfNotPlaying();
        }

        private void ScanForAtoms()
        {
            foreach (var atom in SuperController.singleton.GetAtoms())
            {
                TryConnectAtom(atom);
            }
        }

        public void OnTimelineAnimationReady(JSONStorable storable)
        {
            var link = TryConnectAtom(storable);
            if (GetOrDispose(_mainLinkedAnimation)?.storable == storable)
            {
                RequestControlPanelInjection();
            }
        }

        public void OnTimelineAnimationDisabled(JSONStorable storable)
        {
            var link = _links.FirstOrDefault(l => l.storable == storable);
            if (link == null) return;
            _links.Remove(link);
            _atomsJSON.choices = _links.Select(l => l.storable.containingAtom.uid).ToList();
            if (_mainLinkedAnimation == link)
                _atomsJSON.val = _links.Select(GetOrDispose).FirstOrDefault()?.storable.containingAtom.uid;
            link.Dispose();
        }

        private SyncProxy TryConnectAtom(Atom atom)
        {
            if (atom == null) return null;

            // TODO: If it already exists, do not re-create unless the source storable has been destroyed.

            var storableId = atom.GetStorableIDs().FirstOrDefault(id => id.EndsWith("VamTimeline.AtomPlugin"));
            if (storableId == null) return null;
            var storable = atom.GetStorableByID(storableId);
            return TryConnectAtom(storable);
        }

        private SyncProxy TryConnectAtom(JSONStorable storable)
        {
            foreach (var l in _links.ToArray())
            {
                GetOrDispose(l);
            }
            var existing = _links.FirstOrDefault(a => a.storable == storable);
            if (existing != null) { return existing; }

            var proxy = new SyncProxy()
            {
                storable = storable
            };

            storable.SendMessage(nameof(IRemoteAtomPlugin.VamTimelineConnectController), proxy.dict, SendMessageOptions.RequireReceiver);

            if (!proxy.connected)
            {
                proxy.Dispose();
                return null;
            }

            _links.Add(proxy);
            _links.Sort((SyncProxy s1, SyncProxy s2) => string.Compare(s1.storable.containingAtom.name, s2.storable.containingAtom.name));
            // TODO: Instead of re-creating the list every time, assign once?
            _atomsJSON.choices = _atomsJSON.choices.Concat(new[] { proxy.storable.containingAtom.uid }).ToList();

            if (_mainLinkedAnimation == null)
            {
                _mainLinkedAnimation = proxy;
                proxy.main = true;
                _atomsJSON.val = proxy.storable.containingAtom.uid;
            }

            return proxy;
        }

        private void Hide(bool val)
        {
            if (val)
                OnDisable();
            else
                OnEnable();
        }

        private void InitCustomUI()
        {
            var resyncButton = CreateButton("Re-Sync Atom Plugins");
            resyncButton.button.onClick.AddListener(() =>
            {
                ScanForAtoms();
            });

            CreateToggle(_autoPlayJSON, true);

            CreateToggle(_hideJSON, true);

            CreateToggle(_enableKeyboardShortcuts, true);
        }

        #endregion

        #region Lifecycle

        public void OnEnable()
        {
            if (_atom == null || _ui != null) return;

            try
            {
                _ui = new SimpleSignUI(_atom, this);
                _ui.CreateUIToggleInCanvas(_lockedJSON);
                _ui.CreateUIPopupInCanvas(_atomsJSON);
                _controlPanelSpacer = _ui.CreateUISpacerInCanvas(980f);
                ScanForAtoms();
                if (_mainLinkedAnimation != null)
                    RequestControlPanelInjection();
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline.{nameof(ControllerPlugin)}.{nameof(OnEnable)}: " + exc);
            }
        }

        public void OnDisable()
        {
            if (_ui == null) return;

            try
            {
                foreach (var link in _links)
                {
                    link.Dispose();
                }
                _links.Clear();
                DestroyControlPanelContainer();
                _timeJSON.slider = null;
                _ui.Dispose();
                _ui = null;
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline.{nameof(ControllerPlugin)}.{nameof(OnDisable)}: " + exc);
            }
        }

        public void OnDestroy()
        {
            OnDisable();
        }

        #endregion

        public void OnTimelineAnimationParametersChanged(JSONStorable storable)
        {
            var proxy = GetOrDispose(_mainLinkedAnimation);
            if (proxy == null || proxy.storable != storable)
                return;

            OnTimelineTimeChanged(storable);

            var remoteTime = proxy.time;
            _timeJSON.max = remoteTime.max;
            _timeJSON.valNoCallback = remoteTime.val;
            var remoteAnimation = proxy.animation;
            _animationJSON.choices = remoteAnimation.choices;
            _animationJSON.valNoCallback = remoteAnimation.val;
            _lockedJSON.valNoCallback = proxy.locked.val;
        }

        public void OnTimelineTimeChanged(JSONStorable storable)
        {
            if (_ignoreVamTimelineAnimationFrameUpdated) return;
            _ignoreVamTimelineAnimationFrameUpdated = true;

            try
            {
                var proxy = GetOrDispose(_mainLinkedAnimation);
                if (proxy == null || proxy.storable != storable)
                    return;

                var animationName = proxy.animation.val;
                var isPlaying = proxy.isPlaying.val;
                var time = proxy.time.val;

                foreach (var slave in _links.Where(l => l != proxy).Select(GetOrDispose))
                {
                    var slaveAnimation = slave.animation;
                    if (slaveAnimation.val != animationName && slaveAnimation.choices.Contains(animationName))
                        slave.animation.val = animationName;

                    if (isPlaying)
                        slave.playIfNotPlaying.actionCallback();
                    else
                        slave.stop.actionCallback();

                    var slaveTime = slave.time;
                    if (slaveTime.val < time - 0.0005f || slaveTime.val > time + 0.0005f)
                        slaveTime.val = time;
                }
            }
            finally
            {
                _ignoreVamTimelineAnimationFrameUpdated = false;
            }
        }

        public void Update()
        {
            try
            {
                var proxy = GetOrDispose(_mainLinkedAnimation);
                if (proxy == null) return;

                var time = proxy.time;
                if (time != null && time.val != _timeJSON.val)
                {
                    _timeJSON.valNoCallback = time.val;
                }

                HandleKeyboardShortcuts();
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline.{nameof(ControllerPlugin)}.{nameof(Update)}: " + exc);
                _atomsJSON.val = "";
            }
        }

        private void HandleKeyboardShortcuts()
        {
            var proxy = GetOrDispose(_mainLinkedAnimation);
            if (proxy == null) return;
            if (!_enableKeyboardShortcuts.val) return;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                PreviousFrame();
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                NextFrame();
            }
            else if (Input.GetKeyDown(KeyCode.Space))
            {
                if (proxy.isPlaying.val)
                    Stop();
                else
                    Play();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                Stop();
            }
            else if (Input.GetKeyDown(KeyCode.PageUp))
            {
                if (_atomsJSON.choices.Count > 1 && _atomsJSON.val != _atomsJSON.choices[0])
                    _atomsJSON.val = _atomsJSON.choices.ElementAtOrDefault(_atomsJSON.choices.IndexOf(_atomsJSON.val) - 1);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                if (_atomsJSON.choices.Count > 1 && _atomsJSON.val != _atomsJSON.choices[_atomsJSON.choices.Count - 1])
                    _atomsJSON.val = _atomsJSON.choices.ElementAtOrDefault(_atomsJSON.choices.IndexOf(_atomsJSON.val) + 1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(0));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(1));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(2));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(3));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(4));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(5));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(6));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha8))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(7));
            }
            else if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                ChangeAnimation(proxy.animation.choices.ElementAtOrDefault(8));
            }
        }

        private void Lock(bool val)
        {
            foreach (var animation in _links)
            {
                animation.locked.val = val;
            }
        }

        private void SelectCurrentAtom(string uid)
        {
            if (_mainLinkedAnimation != null)
            {
                _mainLinkedAnimation.main = false;
                _mainLinkedAnimation = null;
            }
            if (string.IsNullOrEmpty(uid))
            {
                return;
            }
            var mainLinkedAnimation = _links.Select(GetOrDispose).FirstOrDefault(la => la.storable.containingAtom.uid == uid);
            if (mainLinkedAnimation == null)
            {
                _atomsJSON.valNoCallback = "";
                return;
            }

            _mainLinkedAnimation = mainLinkedAnimation;
            _mainLinkedAnimation.main = true;
            RequestControlPanelInjection();

            _atomsJSON.valNoCallback = _mainLinkedAnimation.storable.containingAtom.uid;
        }

        private void RequestControlPanelInjection()
        {
            if (_controlPanelSpacer == null) return;

            DestroyControlPanelContainer();

            var proxy = GetOrDispose(_mainLinkedAnimation);
            if (proxy == null) return;

            _controlPanelContainer = new GameObject();
            _controlPanelContainer.transform.SetParent(_controlPanelSpacer.transform, false);

            var rect = _controlPanelContainer.AddComponent<RectTransform>();
            rect.StretchParent();

            proxy.storable.SendMessage(nameof(IRemoteAtomPlugin.VamTimelineRequestControlPanel), _controlPanelContainer, SendMessageOptions.RequireReceiver);
        }

        private void DestroyControlPanelContainer()
        {
            if (_controlPanelContainer == null) return;
            _controlPanelContainer.transform.SetParent(null, false);
            Destroy(_controlPanelContainer);
            _controlPanelContainer = null;
        }

        private SyncProxy GetOrDispose(SyncProxy proxy)
        {
            if (proxy == null) return null;
            if (proxy.storable == null)
            {
                var link = _links.FirstOrDefault(l => l == proxy);
                if (link != null)
                {
                    _links.Remove(link);
                }
                proxy.Dispose();
                if (_mainLinkedAnimation == proxy)
                    _mainLinkedAnimation = null;
                return null;
            }
            return proxy;
        }

        private void ChangeAnimation(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (var la in _links.Select(GetOrDispose))
            {
                if (la.animation.choices.Contains(name))
                    la.animation.val = name;
            }
        }

        private void Play()
        {
            GetOrDispose(_mainLinkedAnimation)?.play.actionCallback();
        }

        private void PlayIfNotPlaying()
        {
            GetOrDispose(_mainLinkedAnimation)?.playIfNotPlaying.actionCallback();
        }

        private void Stop()
        {
            GetOrDispose(_mainLinkedAnimation)?.stop.actionCallback();
        }

        private void NextFrame()
        {
            GetOrDispose(_mainLinkedAnimation)?.nextFrame.actionCallback();
        }

        private void PreviousFrame()
        {
            GetOrDispose(_mainLinkedAnimation)?.previousFrame.actionCallback();
        }
    }
}
