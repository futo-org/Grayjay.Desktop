using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync.Models;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Channel;
using Grayjay.Engine.Models.Feed;
using Microsoft.AspNetCore.Connections.Features;
using System;
using System.Dynamic;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using static Grayjay.ClientServer.Controllers.StateUI;
using static System.Formats.Asn1.AsnWriter;

namespace Grayjay.ClientServer.States;

public class StateBackup
{


    public static List<IManagedStore> GetAllMigrationStores()
    {
        return StateSubscriptions.ToMigrateCheck()
            .Concat(StatePlaylists.ToMigrateCheck())
            .ToList();
    }

    public static void ImportOld(ExportStructure export)
    {
        List<IManagedStore> availableStores = GetAllMigrationStores();
        Dictionary<string, string> unknownPlugins = export.Plugins.Where(x => !StatePlugins.HasPlugin(x.Key)).ToDictionary(x => x.Key, y => y.Value);

        var doImport = false;
        var doImportSettings = false;
        var doImportPlugins = false;
        var doImportPluginSettings = false;
        var doEnablePlugins = false;
        var doImportStores = false;
        Logger.i(nameof(StateBackup), "Starting import choices");

        _ = StateUI.MultiDialog(new List<StateUI.DialogDescriptor>()
        {
            new StateUI.DialogDescriptor()
            {
                Text = "Do you want to import data?",
                TextDetails = "Several dialogs will follow asking individual parts",
                Code = "TODO",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Import", () =>
                    {
                        doImport = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("Cancel", () =>
                    {
                        doImport = false;
                    }, StateUI.ActionStyle.None)
                }
            },
            new StateUI.DialogDescriptor()
            {
                Text = "Would you like to import settings",
                TextDetails = "These are the settings that configure how your app works",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Yes", () =>
                    {
                        doImportSettings = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("No", () =>
                    {
                        doImportSettings = false;
                    }, StateUI.ActionStyle.None)
                }
            }.WithCondition(()=>doImport && export.Settings != null),
            new StateUI.DialogDescriptor()
            {
                Text = "Would you like to import plugins?",
                TextDetails = "Your import contains the following plugins",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Yes", () =>
                    {
                        doImportPlugins = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("No", () =>
                    {
                        doImportPlugins = false;
                    }, StateUI.ActionStyle.None)
                }
            }.WithCondition(()=>doImport && (unknownPlugins?.Count ?? 0) > 0),
            new StateUI.DialogDescriptor()
            {
                Text = "Would you like to import plugin settings?",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Yes", () =>
                    {
                        doImportPluginSettings = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("No", () =>
                    {
                        doImportPluginSettings = false;
                    }, StateUI.ActionStyle.None)
                }
            }.WithCondition(()=>doImport && (export.PluginSettings?.Count ?? 0) > 0),
            new StateUI.DialogDescriptor()
            {
                Text = "Would you like to enable all plugins?",
                TextDetails = "Enabling all plugins ensures all required plugins are available during import",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Yes", () =>
                    {
                        doEnablePlugins = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("No", () =>
                    {
                        doEnablePlugins = false;
                    }, StateUI.ActionStyle.None)
                }
            }.WithCondition(()=>doImport),
            new StateUI.DialogDescriptor()
            {
                Text = "Would you like to import stores?",
                TextDetails = "Stores contain playlists, watch later, subscriptions, etc",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction("Yes", () =>
                    {
                        doImportStores = true;
                    }, StateUI.ActionStyle.Primary),
                    new StateUI.DialogAction("No", () =>
                    {
                        doImportStores = false;
                    }, StateUI.ActionStyle.None)
                }
            }.WithCondition(()=>doImport && (export.Stores?.Count ?? 0) > 0),

        }, async () =>
        {
            Logger.i(nameof(StateBackup), "Starting import");
            if (!doImport)
                return;

            if(doImportPlugins)
            {
                foreach (var unknownPlugin in unknownPlugins)
                {
                    try
                    {
                        StatePlugins.InstallPlugin(unknownPlugin.Value);
                    }
                    catch(PluginConfigInstallException exc)
                    {
                        bool cont = false; ;
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
                    }
                    catch(Exception ex)
                    {
                        bool cont = false; ;
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
                    }
                }
            }


            var enabledBefore = StatePlatform.GetEnabledClients().Select(x => x.ID);

            if (doImportSettings && export.Settings != null)
            {
                Logger.i(nameof(StateBackup), "Importing settings");
                try
                {
                    //TODO: Replace settings
                }
                catch (Exception ex)
                {
                    StateUI.Toast("failed to import settings\n(" + ex.Message + ")");
                }
            }

            if(doEnablePlugins)
            {
                var availableClients = StatePlatform.GetAvailableClients();

                Logger.i(nameof(StateBackup), $"Import enabling plugins [{string.Join(", ", availableClients.Select(x => x.Config.Name))}");
                await StatePlatform.UpdateAvailableClients(false);
                await StatePlatform.SelectClients(availableClients.Select(x => x.ID).ToArray());
            }
            if(doImportPluginSettings)
            {
                foreach(var setting in export.PluginSettings)
                {
                    var plugin = StatePlugins.GetPlugin(setting.Key);
                    if (plugin != null)
                    {
                        Logger.i(nameof(StateBackup), "Importing plugin setting [" + setting.Key + "]");
                        plugin.Settings = setting.Value;
                        StatePlugins.UpdatePlugin(plugin.Config.ID);
                    }
                }
            }
            if(doImportStores)
            {
                foreach(var store in export.Stores)
                {
                    try
                    {
                        Logger.i(nameof(StateBackup), $"Importing store [{store.Key}]");
                        var relevantStore = availableStores.FirstOrDefault(x => x.Name.Equals(store.Key, StringComparison.OrdinalIgnoreCase));
                        if (relevantStore == null)
                        {
                            Logger.w(nameof(StateBackup), $"Unknown store [{store.Key}] import");
                            continue;
                        }


                        int progress = 1;
                        int total = 10;

                        dynamic model = new ExpandoObject();
                        model.storeName = store.Key.Capitalize();
                        model.status = "choice";
                        model.progress = 0;
                        model.total = 0;

                        

                        var dialog = await StateUI.DialogCustom("import", model, new Dictionary<string, Action<CustomDialog, JsonElement>>()
                        {
                            { "choice", async (dialog, obj) =>
                            {
                                if(obj.GetString() == "import")
                                {
                                    model.status = "importing";
                                    model.progress = 0;
                                     model.total = 15;
                                    dialog.UpdateData(model);

                                    var migrationResult = await relevantStore.ImportReconstructions(store.Value, export.Cache, (progress, total) =>
                                    {
                                        Logger.i(nameof(StateBackup), $"Import progress [{progress}/{total}]");
                                        _ = dialog.UpdateData(new RouteValueDictionary(new
                                        {
                                            status = "importing",
                                            progress = progress,
                                            total = total
                                        }).ToDictionary()!);
                                    });

                                    var realFailures = migrationResult.Exceptions.Where(x => !(x is NoPlatformClientException)).ToList();
                                    var pluginFailures = migrationResult.Exceptions.Where(x => (x is NoPlatformClientException)).ToList();

                                    model.messages = migrationResult.Messages.ToArray();
                                    model.exceptions = migrationResult.Exceptions.Select(x=>x.Message).ToArray();
                                    model.status = "finished";
                                    dialog.UpdateData(model);
                                }
                                else
                                    dialog.Close();
                            }},
                            { "close", (dialog, obj) =>
                            {

                            }}
                        });
                    }
                    catch(Exception ex)
                    {
                        Logger.e(nameof(StateBackup), $"Failed to reconstruct import [{store.Key}]", ex);
                        await StateUI.Dialog("", "Import failed", $"Failed to reconstruct import, ignoring store [{store.Key}]", ex.Message, 0, new StateUI.DialogAction("Ok", () => { }));
                    }
                }
            }


            await StatePlatform.SelectClients(enabledBefore.ToArray());
            StateUI.Dialog(null, "Import has finished", null, null, 0, new StateUI.DialogAction("Ok", () => { }));
        });
    }


    public static void Import(ExportStructure export)
    {
        new ImportStructureDialog(export).Start();
    }



    public static ExportStructure Export()
    {
        var exportInfo = new Dictionary<string, string>()
        {
            {"version", "1"}
        };
        var storesToSave = GetAllMigrationStores().Select(x => (x.Name, x.GetAllReconstructionStrings()))
            .ToDictionary(x => x.Item1, y => y.Item2);
        var settings = GrayjaySettings.ToText(GrayjaySettings.Instance);
        var pluginSettings = StatePlugins.GetPlugins()
            .ToDictionary(x => x.Config.ID, y => y.Settings);
        var pluginUrls = StatePlugins.GetPlugins()
            .ToDictionary(x => x.Config.ID, y => y.Config.SourceUrl);

        var cache = GetCache();

        return new ExportStructure(exportInfo, settings, storesToSave, pluginUrls, pluginSettings, cache);
    }

    public static ImportCache GetCache()
    {
        var allPlaylists = StatePlaylists.All;
        var videos = allPlaylists.SelectMany(x => x.Videos).DistinctBy(x => x.Url).ToList();

        var allSubscriptions = StateSubscriptions.GetSubscriptions();
        var channels = allSubscriptions.Select(x => x.Channel).ToList();
        return new ImportCache()
        {
            Channels = channels,
            Videos = videos
        };
    }









    public class ExportStructure
    {
        public Dictionary<string, string> ExportInfo { get; set; }
        public string Settings { get; set; }
        public Dictionary<string, List<string>> Stores { get; set; }
        public Dictionary<string, string> Plugins { get; set; }
        public Dictionary<string, Dictionary<string, string>> PluginSettings { get; set; }
        public ImportCache Cache { get; set; }

        public ExportStructure(Dictionary<string, string> exportInfo, string settings, Dictionary<string, List<string>> stores, Dictionary<string, string> plugins, Dictionary<string, Dictionary<string, string>> pluginSettings, ImportCache cache = null)
        {
            ExportInfo = exportInfo;
            Settings = settings;
            Stores = stores;
            Plugins = plugins;
            PluginSettings = pluginSettings;
            Cache = cache;
        }

        public static ExportStructure FromZipBytes(byte[] bytes)
        {
            using(MemoryStream str = new MemoryStream(bytes))
            using(ZipArchive archive = new ZipArchive(str))
            {
                return FromZip(archive);
            }
        }
        public static ExportStructure FromZip(ZipArchive zip)
        {

            Dictionary<string, string> exportInfo = new Dictionary<string, string>();
            string settings = null;
            Dictionary<string, List<string>> stores = new Dictionary<string, List<string>>();
            Dictionary<String, string> plugins = new Dictionary<string, string>();
            Dictionary<string, Dictionary<string, string>> pluginSettings = new Dictionary<string, Dictionary<string, string>>();
            List<PlatformVideo> videoCache = new List<PlatformVideo>();
            List<PlatformChannel> channelCache = new List<PlatformChannel>();

            var serializerOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            };


            foreach (var nonStoreEntry in zip.Entries.Where(x => !x.FullName.StartsWith("stores/")))
            {
                try
                {
                    if (!nonStoreEntry.FullName.EndsWith("/"))
                    {

                        switch (nonStoreEntry.Name)
                        {
                            case "exportInfo":
                                using (var str = nonStoreEntry.Open())
                                    exportInfo = JsonSerializer.Deserialize<Dictionary<string, string>>(str);
                                break;
                            case "settings":
                                using (var str = nonStoreEntry.Open())
                                using (StreamReader reader = new StreamReader(str))
                                    settings = reader.ReadToEnd();
                                break;
                            case "plugins":
                                using (var str = nonStoreEntry.Open())
                                    plugins = JsonSerializer.Deserialize<Dictionary<string, string>>(str);
                                break;
                            case "plugin_settings":
                                using (var str = nonStoreEntry.Open())
                                    pluginSettings = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(str);
                                break;
                            case "cache_videos":
                                try
                                {
                                    using (var str = nonStoreEntry.Open())
                                        videoCache = JsonSerializer.Deserialize<List<PlatformVideo>>(str, serializerOptions);
                                }
                                catch (Exception ex)
                                {
                                    Logger.e(nameof(StateBackup), "Couldn't deserializer video cache", ex);
                                }
                                break;
                            case "cache_channels":
                                try
                                {
                                    using (var str = nonStoreEntry.Open())
                                        channelCache = JsonSerializer.Deserialize<List<PlatformChannel>>(str, serializerOptions);
                                }
                                catch(Exception ex)
                                {
                                    Logger.e(nameof(StateBackup), "Couldn't deserialize channel cache", ex);
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to parse zip [{nonStoreEntry?.Name}] due to {ex.Message}");
                }
            }
            foreach (var storeEntry in zip.Entries.Where(x => x.FullName.StartsWith("stores/")))
            {
                try
                {
                    if (!storeEntry.FullName.EndsWith("/"))
                    {
                        using (var str = storeEntry.Open())
                            stores[storeEntry.Name] = JsonSerializer.Deserialize<List<string>>(str);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to parse zip [{storeEntry?.Name}] due to {ex.Message}");
                }
            }

            return new ExportStructure(exportInfo, settings, stores, plugins, pluginSettings, new ImportCache()
            {
                Videos = videoCache,
                Channels = channelCache
            });
        }

    }

    public class ImportCache
    {
        public List<PlatformVideo> Videos { get; set; }
        public List<PlatformChannel> Channels { get; set; }
    }



}