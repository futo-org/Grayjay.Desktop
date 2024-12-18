using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC.Port.States;
using static Grayjay.ClientServer.Controllers.StateUI;
using static Grayjay.ClientServer.States.StateBackup;
using System.Text.Json.Serialization;
using System.Text.Json;
using Grayjay.Desktop.POC;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.Models.Subscriptions;

namespace Grayjay.ClientServer.Dialogs
{
    public class ImportStructureDialog
    {
        private CustomDialog _dialog = null;

        private ExportStructure _export = null;
        private ImportDialogModel _model = null;

        public ImportStructureDialog(ExportStructure export)
        {
            _export = export;
            _model = new ImportDialogModel();

            if (!string.IsNullOrEmpty(export.Settings))
                _model.Settings.Count = 1;
            if (export.Plugins != null && export.Plugins.Count > 0)
                _model.Plugins.Count = export.Plugins.Count;
            if (export.PluginSettings != null && export.PluginSettings.Count > 0)
                _model.PluginSettings.Count = export.PluginSettings.Count;

            foreach (var store in export.Stores)
                _model.Stores.Add(new ImportDialogOption(store.Key, store.Key.Capitalize(), store.Value.Count));
        }


        public async Task Start()
        {
            _dialog = await StateUI.DialogCustom("import", _model, new Dictionary<string, Action<CustomDialog, JsonElement>>()
            {
                { "choice", ActionChoice},
                { "import", ActionImport },
                { "close", ActionClose}
            });
        }

        public async void ActionChoice(CustomDialog dialog, JsonElement obj)
        {
            string parameter = obj.GetString();
            string[] parts = parameter.Split(";");

            if (parts[0] != "import")
            {
                dialog.Close();
                return;
            }

            _model.Importing = new List<ImportDialogOption>();
            string[] toImport = parts[1].Split(",");
            if (toImport.Contains(_model.Settings.ID))
                _model.Importing.Add(_model.Settings);
            if (toImport.Contains(_model.Plugins.ID))
                _model.Importing.Add(_model.Plugins);
            if (toImport.Contains(_model.PluginSettings.ID))
                _model.Importing.Add(_model.PluginSettings);
            _model.Importing.AddRange(_model.Stores.Where(x => toImport.Contains(x.ID)));

            _model.Status = "enablePlugins";
            dialog.UpdateData(_model);

            //Emulate(dialog);
            //return;
        }
        public async void ActionImport(CustomDialog dialog, JsonElement obj)
        {
            _model.Status = "importing";
            dialog.UpdateData(_model);

            bool enablePlugins = obj.GetString() == "true";

            List<IManagedStore> availableStores = GetAllMigrationStores();
            Dictionary<string, string> unknownPlugins = _export.Plugins.Where(x => !StatePlugins.HasPlugin(x.Key)).ToDictionary(x => x.Key, y => y.Value);

            var knownImports = new string[] { "settings", "plugins", "pluginSettings" };
            foreach (var import in _model.Importing.Where(x => knownImports.Contains(x.ID)))
            {
                import.Importing = true;
                await dialog.UpdateData(_model);
                try
                {
                    switch (import.ID)
                    {
                        case "settings":
                            Logger.i(nameof(StateBackup), "Importing settings");
                            try
                            {
                                //TODO: Replace settings
                            }
                            catch (Exception ex)
                            {
                                StateUI.Toast("failed to import settings\n(" + ex.Message + ")");
                                import.Exceptions.Add(ex.Message);
                            }
                            _model.Settings.Imported = true;
                            dialog.UpdateData(_model);
                            break;
                        case "plugins":
                            foreach (var unknownPlugin in unknownPlugins)
                            {
                                try
                                {
                                    StatePlugins.InstallPlugin(unknownPlugin.Value, false);
                                }
                                catch (PluginConfigInstallException exc)
                                {
                                    bool cont = false;
                                    import.Exceptions.Add($"[{exc.Config.Name}] " + exc.Message);
                                    dialog.Close();
                                    await StateUI.Dialog("", $"Failed to retrieve plugin", $"Plugin [{exc.Config.Name}] failed to be retrieved.\nWould you like to continue anyway? This may result in broken data if you they are required.", exc.Message, 0,
                                        new StateUI.DialogAction("Continue Anyway", () =>
                                        {
                                            cont = true;
                                        }, StateUI.ActionStyle.Dangerous),
                                        new StateUI.DialogAction("Cancel", () =>
                                        {
                                            cont = false;
                                        }));
                                    if (!cont)
                                        return;
                                    else
                                    {
                                        await Start();
                                        dialog = _dialog;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    bool cont = false; ;
                                    import.Exceptions.Add(ex.Message);
                                    dialog.Close();
                                    await StateUI.Dialog("", $"Failed to retrieve plugin", $"Plugin [{unknownPlugin.Value}] failed to be retrieved.\nWould you like to continue anyway? This may result in broken data if you they are required.", ex.Message, 0,
                                        new StateUI.DialogAction("Continue Anyway", () =>
                                        {
                                            cont = true;
                                        }, StateUI.ActionStyle.Dangerous),
                                        new StateUI.DialogAction("Cancel", () =>
                                        {
                                            cont = false;
                                        }));
                                    if (!cont)
                                        return;
                                    else
                                    {
                                        await Start();
                                        dialog = _dialog;
                                    }
                                }
                                _model.Plugins.Progress++;
                                dialog.UpdateData(_model);
                            }
                            _model.Plugins.Imported = true;
                            dialog.UpdateData(_model);
                            if (unknownPlugins.Any())
                                await StatePlatform.UpdateAvailableClients(); ;
                            break;
                        case "pluginSettings":
                            foreach (var setting in _export.PluginSettings)
                            {
                                var plugin = StatePlugins.GetPlugin(setting.Key);
                                if (plugin != null)
                                {
                                    Logger.i(nameof(StateBackup), "Importing plugin setting [" + setting.Key + "]");
                                    plugin.Settings = setting.Value;
                                    StatePlugins.UpdatePlugin(plugin.Config.ID);
                                }
                                _model.PluginSettings.Progress++;
                                dialog.UpdateData(_model);
                            }
                            _model.PluginSettings.Imported = true;
                            dialog.UpdateData(_model);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    import.Exceptions.Add(ex.Message);
                }
                finally
                {
                    import.Imported = true;
                    dialog.UpdateData(_model);
                }
            }


            var enabledBefore = StatePlatform.GetEnabledClients().Select(x => x.ID);
            if (enablePlugins)
            {
                var availableClients = StatePlatform.GetAvailableClients();

                Logger.i(nameof(StateBackup), $"Import enabling plugins [{string.Join(", ", availableClients.Select(x => x.Config.Name))}");
                await StatePlatform.UpdateAvailableClients(false);
                await StatePlatform.SelectClients(availableClients.Select(x => x.ID).ToArray());
            }

            foreach (var import in _model.Importing.Where(x => !knownImports.Contains(x.ID)))
            {
                import.Importing = true;
                dialog.UpdateData(_model);
                Logger.i(nameof(StateBackup), $"Importing store [{import.ID}]");
                var store = _export.Stores.FirstOrDefault(x => x.Key == import.ID);
                if (store.Value == null)
                    continue;
                var relevantStore = availableStores.FirstOrDefault(x => x.Name.Equals(store.Key, StringComparison.OrdinalIgnoreCase));
                if (relevantStore != null)
                {
                    try
                    {

                        var migrationResult = await relevantStore.ImportReconstructions(store.Value, _export.Cache, async (progress, total) =>
                        {
                            Logger.i(nameof(StateBackup), $"Import progress [{progress}/{total}]");
                            import.Progress = progress;
                            _model.ImportStatus = $"Importing {import.Progress} out of {import.Count} items...";
                            await dialog.UpdateData(_model);
                        });

                        var realFailures = migrationResult.Exceptions.Where(x => !(x is NoPlatformClientException)).ToList();
                        var pluginFailures = migrationResult.Exceptions.Where(x => (x is NoPlatformClientException)).ToList();

                        import.Warnings = migrationResult.Messages.ToList();
                        import.Exceptions = migrationResult.Exceptions.Select(x => x.Message).ToList();
                        await dialog.UpdateData(_model);
                    }
                    catch (Exception ex)
                    {
                        import.Exceptions.Add(ex.Message);
                        Logger.e(nameof(StateBackup), $"Failed to reconstruct import [{store.Key}]", ex);
                    }
                    finally
                    {
                        import.Imported = true;
                        dialog.UpdateData(_model);
                    }
                }
                else if(store.Key == "subscription_groups")
                {
                    try
                    {
                        var groups = store.Value.Select(x => (SubscriptionGroup)GJsonSerializer.AndroidCompatible.DeserializeObj(x, typeof(SubscriptionGroup))).ToList();
                        foreach(var group in groups)
                        {
                            StateSubscriptions.SaveGroup(group);
                        }
                    }
                    catch(Exception ex)
                    {
                        import.Exceptions.Add(ex.Message);
                        Logger.e(nameof(StateBackup), $"Failed to import [{store.Key}]", ex);
                    }
                    finally
                    {
                        import.Imported = true;
                        dialog.UpdateData(_model);
                    }
                }
                else if(store.Key == "history")
                {

                }
                else
                {
                    Logger.w(nameof(StateBackup), $"Unknown store [{import.ID}] import");
                    continue;
                }
            }
            await StatePlatform.SelectClients(enabledBefore.ToArray());

            _model.Status = "finished";
            await dialog.UpdateData(_model);
        }
        public void ActionClose(CustomDialog dialog, JsonElement obj)
        {

        }

        public void Emulate(CustomDialog dialog)
        {

            foreach (var import in _model.Importing)
            {
                import.Importing = true;
                dialog.UpdateData(_model);
                switch (import.ID)
                {
                    case "settings":
                        for (int i = 0; i < import.Count; i++)
                        {
                            import.Progress++;
                            _model.ImportStatus = $"Importing {import.Progress} out of {import.Count} items...";
                            dialog.UpdateData(_model);
                            Thread.Sleep(300);
                        }
                        break;
                    case "plugins":

                        for (int i = 0; i < import.Count; i++)
                        {
                            import.Progress++;
                            _model.ImportStatus = $"Importing {import.Progress} out of {import.Count} items...";
                            dialog.UpdateData(_model);
                            Thread.Sleep(300);
                        }
                        break;
                    case "pluginSettings":

                        for (int i = 0; i < import.Count; i++)
                        {
                            import.Progress++;
                            _model.ImportStatus = $"Importing {import.Progress} out of {import.Count} items...";
                            if (i == 2)
                            {
                                import.Exceptions.Add("Failed for some reason on item 2");
                                import.Warnings.Add("Test warning for item 2");
                                import.Warnings.Add("Another warning for item 2");
                            }
                            dialog.UpdateData(_model);
                            Thread.Sleep(300);
                        }
                        break;
                    default:
                        var store = _export.Stores.FirstOrDefault(x => x.Key == import.ID);

                        for (int i = 0; i < import.Count; i++)
                        {
                            import.Progress++;
                            _model.ImportStatus = $"Importing {import.Progress} out of {import.Count} items...";
                            dialog.UpdateData(_model);
                            Thread.Sleep(300);
                        }

                        break;
                }
                import.Imported = true;
                dialog.UpdateData(_model);
            }
            _model.Status = "finished";
            dialog.UpdateData(_model);
        }


        public class ImportDialogModel
        {
            public string Status { get; set; } = "choice";
            public string ImportStatus { get; set; }

            public ImportDialogOption Settings { get; set; } = new ImportDialogOption("settings", "Settings", 0);
            public ImportDialogOption Plugins { get; set; } = new ImportDialogOption("plugins", "Plugins", 0);
            public ImportDialogOption PluginSettings { get; set; } = new ImportDialogOption("pluginSettings", "Plugin Settings", 0);

            public List<ImportDialogOption> Stores { get; set; } = new List<ImportDialogOption>();

            public List<ImportDialogOption> Importing { get; set; }
        }

        public class ImportDialogOption
        {
            [JsonPropertyName("ID")]
            public string ID { get; set; }
            [JsonPropertyName("Name")]
            public string Name { get; set; }
            [JsonPropertyName("Count")]
            public int Count { get; set; }
            [JsonPropertyName("Importing")]
            public bool Importing { get; set; }
            [JsonPropertyName("Imported")]
            public bool Imported { get; set; }
            [JsonPropertyName("Progress")]
            public int Progress { get; set; }

            [JsonPropertyName("Warnings")]
            public List<string> Warnings { get; set; } = new List<string>();
            [JsonPropertyName("Exceptions")]
            public List<string> Exceptions { get; set; } = new List<string>();

            public ImportDialogOption(string id, string name, int count)
            {
                ID = id;
                Name = name;
                Count = count;
            }
        }
    }
}
