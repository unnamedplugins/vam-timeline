using System;
using System.Collections.Generic;
using System.Linq;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace VamTimeline
{
    public class ImportExportScreen : ScreenBase
    {
        private const string _saveExt = "json";
        private const string _saveFolder = "Saves\\animations";
        private const string _poseAndAllAnimations = "Pose & all animations";
        private const string _allAnimations = "All animations";

        public const string ScreenName = "Import / Export";

        public override string screenId => ScreenName;

        private JSONStorableStringChooser _exportAnimationsJSON;

        public ImportExportScreen()
            : base()
        {
        }

        public override void Init(IAtomPlugin plugin)
        {
            base.Init(plugin);

            CreateChangeScreenButton("<b><</b> <i>Back</i>", MoreScreen.ScreenName);

            prefabFactory.CreateSpacer();

            InitImportUI();

            prefabFactory.CreateSpacer();

            InitExportUI();
        }

        private void InitImportUI()
        {
            var importUI = prefabFactory.CreateButton("Import animation(s)");
            importUI.button.onClick.AddListener(() => Import());
        }

        private void InitExportUI()
        {
            _exportAnimationsJSON = new JSONStorableStringChooser("Animation to export", new List<string> { _poseAndAllAnimations, _allAnimations }.Concat(animation.clips.Select(c => c.animationName)).ToList(), _poseAndAllAnimations, "Animation to export")
            {
                isStorable = false
            };
            var exportAnimationsUI = prefabFactory.CreateScrollablePopup(_exportAnimationsJSON);

            var exportUI = prefabFactory.CreateButton("Export animation");
            exportUI.button.onClick.AddListener(() => Export());
        }

        private void Export()
        {
            try
            {
                FileManagerSecure.CreateDirectory(_saveFolder);
                var fileBrowserUI = SuperController.singleton.fileBrowserUI;
                fileBrowserUI.SetTitle("Save animation");
                fileBrowserUI.fileRemovePrefix = null;
                fileBrowserUI.hideExtension = false;
                fileBrowserUI.keepOpen = false;
                fileBrowserUI.fileFormat = _saveExt;
                fileBrowserUI.defaultPath = _saveFolder;
                fileBrowserUI.showDirs = true;
                fileBrowserUI.shortCuts = null;
                fileBrowserUI.browseVarFilesAsDirectories = false;
                fileBrowserUI.SetTextEntry(true);
                fileBrowserUI.Show(ExportFileSelected);
                fileBrowserUI.ActivateFileNameField();
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline: Failed to save file dialog: {exc}");
            }
        }

        private void ExportFileSelected(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!path.ToLower().EndsWith($".{_saveExt}")) path += $".{_saveExt}";

            try
            {
                var jc = plugin.GetAnimationJSON(_exportAnimationsJSON.val == _poseAndAllAnimations || _exportAnimationsJSON.val == _allAnimations ? null : _exportAnimationsJSON.val);
                jc["AtomType"] = plugin.containingAtom.type;
                var atomState = new JSONClass();
                IEnumerable<FreeControllerV3> controllers;
                if (_exportAnimationsJSON.val == _poseAndAllAnimations)
                    controllers = plugin.containingAtom.freeControllers;
                else if (_exportAnimationsJSON.val == _allAnimations)
                    controllers = animation.clips
                          .SelectMany(c => c.targetControllers)
                          .Select(t => t.controller)
                          .Distinct();
                else
                    controllers = animation.clips
                        .First(c => c.animationName == _exportAnimationsJSON.val)
                        .targetControllers
                        .Select(t => t.controller);
                foreach (var fc in controllers)
                {
                    if (fc.name == "control") continue;
                    if (!fc.name.EndsWith("Control")) continue;
                    atomState[fc.name] = new JSONClass
                    {
                        {"currentPositionState", ((int)fc.currentPositionState).ToString()},
                        {"localPosition", AtomAnimationSerializer.SerializeVector3(fc.transform.localPosition)},
                        {"currentRotationState", ((int)fc.currentRotationState).ToString()},
                        {"localRotation", AtomAnimationSerializer.SerializeQuaternion(fc.transform.localRotation)}
                    };
                }
                jc["ControllersState"] = atomState;
                SuperController.singleton.SaveJSON(jc, path);
                SuperController.singleton.DoSaveScreenshot(path);
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline: Failed to export animation: {exc}");
            }
        }

        private void Import()
        {
            try
            {
                FileManagerSecure.CreateDirectory(_saveFolder);
                var shortcuts = FileManagerSecure.GetShortCutsForDirectory(_saveFolder);
                SuperController.singleton.GetMediaPathDialog(ImportFileSelected, _saveExt, _saveFolder, false, true, false, null, false, shortcuts);
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline: Failed to open file dialog: {exc}");
            }
        }

        private void ImportFileSelected(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var json = SuperController.singleton.LoadJSON(path);
                if (json["AtomType"]?.Value != plugin.containingAtom.type)
                {
                    SuperController.LogError($"VamTimeline: Loaded animation for {json["AtomType"]} but current atom type is {plugin.containingAtom.type}");
                    return;
                }

                var jc = json.AsObject;
                if (jc.HasKey("ControllersState"))
                {
                    var controllersState = jc["ControllersState"].AsObject;
                    foreach (var k in controllersState.Keys)
                    {
                        var fc = plugin.containingAtom.freeControllers.FirstOrDefault(x => x.name == k);
                        if (fc == null)
                        {
                            SuperController.LogError($"VamTimeline: Loaded animation had state for controller {k} but no such controller were found on this atom.");
                            continue;
                        }
                        var state = controllersState[k];
                        fc.currentPositionState = (FreeControllerV3.PositionState)state["currentPositionState"].AsInt;
                        fc.transform.localPosition = AtomAnimationSerializer.DeserializeVector3(state["localPosition"].AsObject);
                        fc.currentRotationState = (FreeControllerV3.RotationState)state["currentRotationState"].AsInt;
                        fc.transform.localRotation = AtomAnimationSerializer.DeserializeQuaternion(state["localRotation"].AsObject);
                    }
                }

                plugin.serializer.DeserializeAnimation(animation, json.AsObject);
                var lastAnimation = animation.clips.Select(c => c.animationName).LastOrDefault();
                // NOTE: Because the animation instance changes, we'll end up with the _old_ "current" not being updated.
                if (lastAnimation != animation.current.animationName)
                    plugin.ChangeAnimation(lastAnimation);
                else
                    animation.SelectAnimation(lastAnimation);
                animation.Sample();
            }
            catch (Exception exc)
            {
                SuperController.LogError($"VamTimeline.{nameof(AdvancedKeyframeToolsScreen)}.{nameof(ImportFileSelected)}: Failed to import animation: {exc}");
            }
        }
    }
}
