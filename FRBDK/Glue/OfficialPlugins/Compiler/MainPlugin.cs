﻿using FlatRedBall.Glue.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlatRedBall.Glue.Plugins.Interfaces;
using System.ComponentModel.Composition;
using OfficialPlugins.Compiler.ViewModels;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.Managers;
using System.Windows;
using OfficialPlugins.Compiler.CodeGeneration;
using System.Net.Sockets;
using OfficialPlugins.Compiler.Managers;
using FlatRedBall.Glue.Controls;
using System.ComponentModel;
using FlatRedBall.Glue.IO;
using Newtonsoft.Json;
using OfficialPlugins.Compiler.Models;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.IO;
using OfficialPluginsCore.Compiler.ViewModels;
using OfficialPluginsCore.Compiler.Managers;
using System.Diagnostics;
using System.Timers;
using Glue;
using OfficialPluginsCore.Compiler.CommandReceiving;
using FlatRedBall.Glue.Elements;
using OfficialPlugins.Compiler.Dtos;
using OfficialPlugins.Compiler.CommandSending;

namespace OfficialPlugins.Compiler
{
    [Export(typeof(PluginBase))]
    public class MainPlugin : PluginBase
    {
        #region Fields/Properties

        MainControl control;

        Compiler compiler;
        Runner runner;
        CompilerViewModel viewModel;
        GlueViewSettingsViewModel glueViewSettingsViewModel;

        public static CompilerViewModel MainViewModel { get; private set; }

        PluginTab buildTab;
        PluginTab glueViewSettingsTab;

        Game1GlueControlGenerator game1GlueControlGenerator;

        public override string FriendlyName => "Glue Compiler";

        public override Version Version
        {
            get
            {
                // 0.4 introduces:
                // - multicore building
                // - Removed warnings and information when building - now we just show start, end, and errors
                // - If an error occurs, a popup appears telling the user that the game crashed, and to open Visual Studio
                // 0.5
                // - Support for running content-only builds
                // 0.6
                // - Added VS 2017 support
                // 0.7
                // - Added a list of MSBuild locations
                return new Version(0, 7);
            }
        }

        FilePath JsonSettingsFilePath => GlueState.Self.ProjectSpecificSettingsFolder + "CompilerSettings.json";

        bool ignoreViewModelChanges = false;

        Timer timer;

        #endregion

        #region Startup

        public override void StartUp()
        {
            CreateControl();

            CreateToolbar();

            RefreshManager.Self.InitializeEvents(this.control.PrintOutput, this.control.PrintOutput);

            Output.Initialize(this.control.PrintOutput);


            compiler = Compiler.Self;
            runner = Runner.Self;

            game1GlueControlGenerator = new Game1GlueControlGenerator();
            this.RegisterCodeGenerator(game1GlueControlGenerator);

            this.RegisterCodeGenerator(new CompilerPluginElementCodeGenerator());

            // do this after creating the compiler, view model, and control
            AssignEvents();

            #region Start the timer

            var timerFrequency = 250; // ms
            timer = new Timer(timerFrequency);
            timer.Elapsed += HandleTimerElapsed;
            timer.SynchronizingObject = MainGlueWindow.Self;
            timer.Start();

            #endregion
        }

        private void AssignEvents()
        {
            var manager = new FileChangeManager(control, compiler, viewModel);
            this.ReactToFileChangeHandler += manager.HandleFileChanged;
            this.ReactToLoadedGlux += HandleGluxLoaded;
            this.ReactToUnloadedGlux += HandleGluxUnloaded;
            this.ReactToNewFileHandler += RefreshManager.Self.HandleNewFile;

            this.ReactToCodeFileChange += RefreshManager.Self.HandleFileChanged;
            this.NewEntityCreated += RefreshManager.Self.HandleNewEntityCreated;


            this.NewScreenCreated += (newScreen) =>
            {
                ToolbarController.Self.HandleNewScreenCreated(newScreen);
                RefreshManager.Self.HandleNewScreenCreated();
            };
            this.ReactToScreenRemoved += ToolbarController.Self.HandleScreenRemoved;
            // todo - handle startup changed...
            this.ReactToNewObjectHandler += RefreshManager.Self.HandleNewObjectCreated;
            this.ReactToObjectRemoved += async (owner, nos) =>
                await RefreshManager.Self.HandleObjectRemoved(owner, nos);
            this.ReactToElementVariableChange += RefreshManager.Self.HandleVariableChanged;
            this.ReactToNamedObjectChangedValue += (string changedMember, object oldValue, NamedObjectSave namedObject) => 
                RefreshManager.Self.HandleNamedObjectValueChanged(changedMember, oldValue, namedObject, Dtos.AssignOrRecordOnly.Assign);
            this.ReactToChangedStartupScreen += ToolbarController.Self.ReactToChangedStartupScreen;
            this.ReactToItemSelectHandler += RefreshManager.Self.HandleItemSelected;
            this.ReactToObjectContainerChanged += RefreshManager.Self.HandleObjectContainerChanged;
            // If a variable is added, that may be used later to control initialization.
            // The game won't reflect that until it has been restarted, so let's just take 
            // care of it now. For variable removal I don't know if any restart is needed...
            this.ReactToVariableAdded += RefreshManager.Self.HandleVariableAdded;
            this.ReactToStateCreated += RefreshManager.Self.HandleStateCreated;
            this.ReactToStateVariableChanged += RefreshManager.Self.HandleStateVariableChanged;
        }


        #endregion

        #region Public events (called externally)

        public async Task BuildAndRun()
        {
            if (viewModel.IsToolbarPlayButtonEnabled)
            {
                GlueCommands.Self.DialogCommands.FocusTab("Build");
                var succeeded = await Compile();

                if (succeeded)
                {
                    bool hasErrors = GetIfHasErrors();
                    if (hasErrors)
                    {
                        var runAnywayMessage = "Your project has content errors. To fix them, see the Errors tab. You can still run the game but you may experience crashes. Run anyway?";

                        GlueCommands.Self.DialogCommands.ShowYesNoMessageBox(runAnywayMessage, async () => await runner.Run(preventFocus: false));
                    }
                    else
                    {
                        PluginManager.ReceiveOutput("Building succeeded. Running project...");

                        await runner.Run(preventFocus: false);
                    }
                }
                else
                {
                    PluginManager.ReceiveError("Building failed. See \"Build\" tab for more information.");
                }
            }
        }

        public bool GetIfIsRunningInEditMode()
        {
            return viewModel.IsEditChecked && viewModel.IsRunning;
        }

        public async void MakeGameBorderless(bool isBorderless)
        {
            var dto = new Dtos.SetBorderlessDto
            {
                IsBorderless = isBorderless
            };

            await CommandSending.CommandSender
                .Send(dto, glueViewSettingsViewModel.PortNumber);
        }

        #endregion

        System.Threading.SemaphoreSlim getCommandsSemaphore = new System.Threading.SemaphoreSlim(1);
        private async void HandleTimerElapsed(object sender, ElapsedEventArgs e)
        {
            var isBusy = await getCommandsSemaphore.WaitAsync(0);
            if(!isBusy)
            {
                try
                {
                    if(viewModel.IsEditChecked)
                    {
                        var gameToGlueCommandsAsString = await CommandSending.CommandSender
                            .SendCommand("GetCommands", glueViewSettingsViewModel.PortNumber);

                        if (!string.IsNullOrEmpty(gameToGlueCommandsAsString))
                        {
                            CommandReceiver.HandleCommandsFromGame(gameToGlueCommandsAsString, glueViewSettingsViewModel.PortNumber);
                        }
                    }
                }
                catch
                {
                    // it's okay
                }
                finally
                {
                    getCommandsSemaphore.Release();
                }
            }

        }
        private void HandleGluxUnloaded()
        {
            viewModel.CompileContentButtonVisibility = Visibility.Collapsed;
            viewModel.HasLoadedGlux = false;

            ToolbarController.Self.HandleGluxUnloaded();
        }

        private CompilerSettingsModel LoadOrCreateCompilerSettings()
        {
            CompilerSettingsModel compilerSettings = new CompilerSettingsModel();
            var filePath = JsonSettingsFilePath;
            if (filePath.Exists())
            {
                try
                {
                    var text = System.IO.File.ReadAllText(filePath.FullPath);
                    compilerSettings = JsonConvert.DeserializeObject<CompilerSettingsModel>(text);
                }
                catch
                {
                    // do nothing, it'll just get wiped out and re-saved later
                }
            }

            return compilerSettings;
        }

        private bool IsFrbNewEnough()
        {
            var mainProject = GlueState.Self.CurrentMainProject;
            if(mainProject.IsFrbSourceLinked())
            {
                return true;
            }
            else
            {
                return GlueState.Self.CurrentGlueProject.FileVersion >= (int)GlueProjectSave.GluxVersions.SupportsEditMode;
            }
        }

        private void HandleGluxLoaded()
        {
            UpdateCompileContentVisibility();

            var model = LoadOrCreateCompilerSettings();
            ignoreViewModelChanges = true;
            viewModel.SetFrom(model);
            glueViewSettingsViewModel.SetFrom(model);
            ignoreViewModelChanges = false;

            viewModel.IsGluxVersionNewEnoughForGlueControlGeneration =
                GlueState.Self.CurrentGlueProject.FileVersion >= (int)GlueProjectSave.GluxVersions.AddedGeneratedGame1;
            viewModel.HasLoadedGlux = true;

            game1GlueControlGenerator.PortNumber = model.PortNumber;
            game1GlueControlGenerator.IsGlueControlManagerGenerationEnabled = model.GenerateGlueControlManagerCode && IsFrbNewEnough();
            RefreshManager.Self.PortNumber = model.PortNumber;

            ToolbarController.Self.HandleGluxLoaded();

            if(IsFrbNewEnough())
            {
                TaskManager.Self.Add(() => EmbeddedCodeManager.EmbedAll(model.GenerateGlueControlManagerCode), "Generate Glue Control Code");
            }

            GlueCommands.Self.ProjectCommands.AddNugetIfNotAdded("Newtonsoft.Json", "12.0.3");
        }

        private void UpdateCompileContentVisibility()
        {
            bool shouldShowCompileContentButton = false;

            if (GlueState.Self.CurrentMainProject != null)
            {
                shouldShowCompileContentButton = GlueState.Self.CurrentMainProject != GlueState.Self.CurrentMainContentProject;

                if (!shouldShowCompileContentButton)
                {
                    foreach (var mainSyncedProject in GlueState.Self.SyncedProjects)
                    {
                        if (mainSyncedProject != mainSyncedProject.ContentProject)
                        {
                            shouldShowCompileContentButton = true;
                            break;
                        }
                    }
                }

            }

            if (shouldShowCompileContentButton)
            {
                viewModel.CompileContentButtonVisibility = Visibility.Visible;
            }
            else
            {
                viewModel.CompileContentButtonVisibility = Visibility.Collapsed;
            }
        }

        private void CreateToolbar()
        {
            var toolbar = new RunnerToolbar();
            toolbar.RunClicked += HandleToolbarRunClicked;

            ToolbarController.Self.Initialize(toolbar);

            toolbar.DataContext = ToolbarController.Self.GetViewModel();

            base.AddToToolBar(toolbar, "Standard");
        }

        private async void HandleToolbarRunClicked(object sender, EventArgs e)
        {
            await BuildAndRun();
        }


        private void CreateControl()
        {
            viewModel = new CompilerViewModel();
            viewModel.Configuration = "Debug";
            glueViewSettingsViewModel = new GlueViewSettingsViewModel();
            glueViewSettingsViewModel.PropertyChanged += HandleGlueViewSettingsViewModelPropertyChanged;
            viewModel.PropertyChanged += HandleMainViewModelPropertyChanged;

            MainViewModel = viewModel;

            control = new MainControl();
            control.DataContext = viewModel;

            Runner.Self.ViewModel = viewModel;
            RefreshManager.Self.ViewModel = viewModel;
            RefreshManager.Self.GlueViewSettingsViewModel = glueViewSettingsViewModel;

            VariableSendingManager.Self.ViewModel = viewModel;
            VariableSendingManager.Self.GlueViewSettingsViewModel = glueViewSettingsViewModel;


            buildTab = base.CreateTab(control, "Build", TabLocation.Bottom);
            buildTab.Show();


            var glueViewSettingsView = new Views.GlueViewSettings();
            glueViewSettingsView.ViewModel = glueViewSettingsViewModel;

            glueViewSettingsTab = base.CreateTab(glueViewSettingsView, "GlueView Settings");

            AssignControlEvents();
        }

        private async void HandleGlueViewSettingsViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //////////Early Out////////////////////
            if (ignoreViewModelChanges)
            {
                return;
            }

            /////////End Early Out//////////////// 
            var propertyName = e.PropertyName;
            switch(propertyName)
            {
                case nameof(GlueViewSettingsViewModel.PortNumber):
                    await HandlePortOrGenerateCheckedChanged(propertyName);
                    break;
            }
            throw new NotImplementedException();
        }

        private async void HandleMainViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            //////////Early Out////////////////////
            if (ignoreViewModelChanges)
            {
                return;
            }

            /////////End Early Out////////////////
            var propertyName = e.PropertyName;

            switch (propertyName)
            {
                case nameof(CompilerViewModel.IsGenerateGlueControlManagerInGame1Checked):
                    await HandlePortOrGenerateCheckedChanged(propertyName);

                    break;
                case nameof(CompilerViewModel.CurrentGameSpeed):
                    var speedPercentage = int.Parse(viewModel.CurrentGameSpeed.Substring(0, viewModel.CurrentGameSpeed.Length - 1));
                    await CommandSender.Send(new SetSpeedDto
                    {
                        SpeedPercentage = speedPercentage
                    }, glueViewSettingsViewModel.PortNumber);
                    
                    break;
                case nameof(CompilerViewModel.EffectiveIsRebuildAndRestartEnabled):
                    RefreshManager.Self.IsExplicitlySetRebuildAndRestartEnabled = viewModel.EffectiveIsRebuildAndRestartEnabled;
                    break;
                case nameof(CompilerViewModel.IsToolbarPlayButtonEnabled):
                    ToolbarController.Self.SetEnabled(viewModel.IsToolbarPlayButtonEnabled);
                    break;
                case nameof(CompilerViewModel.IsRunning):
                    //CommandSender.CancelConnect();
                    break;
                case nameof(CompilerViewModel.PlayOrEdit):

                    var inEditMode = viewModel.PlayOrEdit == PlayOrEdit.Edit;
                    await CommandSending.CommandSender.Send(
                        new Dtos.SetEditMode { IsInEditMode = inEditMode },
                        glueViewSettingsViewModel.PortNumber);

                    if (inEditMode)
                    {
                        var currentEntity = GlueCommands.Self.DoOnUiThread<EntitySave>(() => GlueState.Self.CurrentEntitySave);
                        if(currentEntity != null)
                        {
                            await GlueCommands.Self.DoOnUiThread(async () => await RefreshManager.Self.PushGlueSelectionToGame());
                        }
                        else
                        {
                            var screenName = await CommandSending.CommandSender.GetScreenName(glueViewSettingsViewModel.PortNumber);

                            if (!string.IsNullOrEmpty(screenName))
                            {
                                var glueScreenName =
                                    string.Join('\\', screenName.Split('.').Skip(1).ToArray());

                                var screen = ObjectFinder.Self.GetScreenSave(glueScreenName);

                                if (screen != null)
                                {
                                    await GlueCommands.Self.DoOnUiThread(async () =>
                                    {
                                        if(GlueState.Self.CurrentElement != screen)
                                        {
                                            GlueState.Self.CurrentElement = screen;
                                        }
                                        else
                                        {
                                            // the screens are the same, so push the object selection from Glue to the game:
                                            await RefreshManager.Self.PushGlueSelectionToGame();
                                        }
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        // the user is viewing an entity, so force the screen
                        if(GlueState.Self.CurrentEntitySave != null)
                        {
                            // push the selection to game
                            var startupScreen = ObjectFinder.Self.GetScreenSave(GlueState.Self.CurrentGlueProject.StartUpScreen);
                            await RefreshManager.Self.PushGlueSelectionToGame(forcedElement: startupScreen);
                        }
                    }


                    break;
            }
        }

        private async Task HandlePortOrGenerateCheckedChanged(string propertyName)
        {
            control.PrintOutput("Applying changes");
            game1GlueControlGenerator.IsGlueControlManagerGenerationEnabled = viewModel.IsGenerateGlueControlManagerInGame1Checked && IsFrbNewEnough();
            game1GlueControlGenerator.PortNumber = glueViewSettingsViewModel.PortNumber;
            RefreshManager.Self.PortNumber = glueViewSettingsViewModel.PortNumber;
            GlueCommands.Self.GenerateCodeCommands.GenerateGame1();
            var model = new CompilerSettingsModel();
            viewModel.SetModel(model);
            glueViewSettingsViewModel.SetModel(model);
            try
            {
                var text = JsonConvert.SerializeObject(model);
                GlueCommands.Self.TryMultipleTimes(() =>
                {
                    System.IO.Directory.CreateDirectory(JsonSettingsFilePath.GetDirectoryContainingThis().FullPath);
                    System.IO.File.WriteAllText(JsonSettingsFilePath.FullPath, text);
                });
            }
            catch
            {
                // no big deal if it fails
            }
            if (IsFrbNewEnough())
            {
                TaskManager.Self.Add(() => EmbeddedCodeManager.EmbedAll(model.GenerateGlueControlManagerCode), "Generate Glue Control Code");
            }

            if (GlueState.Self.CurrentGlueProject.FileVersion >= (int)GlueProjectSave.GluxVersions.NugetPackageInCsproj)
            {
                GlueCommands.Self.ProjectCommands.AddNugetIfNotAdded("Newtonsoft.Json", "12.0.3");
            }

            RefreshManager.Self.StopAndRestartTask($"{propertyName} changed");

            control.PrintOutput("Waiting for tasks to finish...");
            await TaskManager.Self.WaitForAllTasksFinished();
            control.PrintOutput("Finishined adding/generating code for GlueControlManager");
        }

        private void AssignControlEvents()
        {
            control.BuildClicked += async (not, used) =>
            {
                await Compile();
            };

            control.StopClicked += (not, used) =>
            {
                runner.KillGameProcess();
            };

            control.RestartGameClicked += async (not, used) =>
            {
                viewModel.IsPaused = false;
                runner.KillGameProcess();
                var succeeded = await Compile();
                if (succeeded)
                {
                    await runner.Run(preventFocus: false);
                }
            };

            control.RestartGameCurrentScreenClicked += async (not, used) =>
            {
                var wasEditChecked = viewModel.IsEditChecked;
                var screenName = await CommandSending.CommandSender.GetScreenName(glueViewSettingsViewModel.PortNumber);


                viewModel.IsPaused = false;
                runner.KillGameProcess();
                var succeeded = await Compile();

                if (succeeded)
                {
                    if (succeeded)
                    {
                        await runner.Run(preventFocus: false, screenName);
                        if (wasEditChecked)
                        {
                            viewModel.IsEditChecked = true;
                        }
                    }
                }
            };

            control.RestartScreenClicked += async (not, used) =>
            {
                viewModel.IsPaused = false;
                await CommandSender.Send(new RestartScreenDto(), glueViewSettingsViewModel.PortNumber);
            };

            control.AdvanceOneFrameClicked += async (not, used) =>
            {
                await CommandSender.Send(new AdvanceOneFrameDto(), glueViewSettingsViewModel.PortNumber);
            };

            control.BuildContentClicked += delegate
            {
                BuildContent(OutputSuccessOrFailure);
            };

            control.RunClicked += async (not, used) =>
            {
                var succeeded = await Compile();
                if (succeeded)
                {
                    if (succeeded)
                    {
                        await runner.Run(preventFocus: false);
                    }
                    else
                    {
                        var runAnywayMessage = "Your project has content errors. To fix them, see the Errors tab. You can still run the game but you may experience crashes. Run anyway?";

                        GlueCommands.Self.DialogCommands.ShowYesNoMessageBox(runAnywayMessage, async () => await runner.Run(preventFocus: false));
                    }
                }
            };

            control.PauseClicked += async (not, used) =>
            {
                viewModel.IsPaused = true;
                await CommandSender.Send(new TogglePauseDto(), glueViewSettingsViewModel.PortNumber);
            };

            control.UnpauseClicked += async (not, used) =>
            {
                viewModel.IsPaused = false;
                await CommandSender.Send(new TogglePauseDto(), glueViewSettingsViewModel.PortNumber);
            };

            control.SettingsClicked += (not, used) =>
            {
                ShowSettingsTab();
            };
        }

        void ShowSettingsTab()
        {
            glueViewSettingsTab.Show();
            glueViewSettingsTab.Focus();
        }

        private static bool GetIfHasErrors()
        {
            var errorPlugin = PluginManager.AllPluginContainers
                                .FirstOrDefault(item => item.Plugin is ErrorPlugin.MainErrorPlugin)?.Plugin as ErrorPlugin.MainErrorPlugin;

            var hasErrors = errorPlugin?.HasErrors == true;
            return hasErrors;
        }

        private void OutputSuccessOrFailure(bool succeeded)
        {
            if (succeeded)
            {
                control.PrintOutput($"{DateTime.Now.ToLongTimeString()} Build succeeded");
            }
            else
            {
                control.PrintOutput($"{DateTime.Now.ToLongTimeString()} Build failed");

            }
        }

        private void BuildContent(Action<bool> afterCompile = null)
        {
            compiler.BuildContent(control.PrintOutput, control.PrintOutput, afterCompile, viewModel.Configuration);
        }

        private async Task<bool> Compile()
        {
            viewModel.IsCompiling = true;
            var toReturn = await compiler.Compile(
                control.PrintOutput,
                control.PrintOutput,
                viewModel.Configuration);
            viewModel.IsCompiling = false;
            return toReturn;
        }

        public override bool ShutDown(PluginShutDownReason shutDownReason)
        {
            return true;
        }


        public async void ShowState(string stateName, string categoryName)
        {
            await RefreshManager.Self.PushGlueSelectionToGame(categoryName, stateName);
        }
    }

}
