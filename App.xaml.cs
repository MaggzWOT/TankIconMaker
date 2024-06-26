﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using RT.Util;
using RT.Util.Dialogs;
using RT.Util.Lingo;
using RT.Util.Serialization;
using TankIconMaker.SettingsMigrations;
using WotDataLib;
using WpfCrutches;
using D = System.Drawing;
using W = System.Windows.Media;

namespace TankIconMaker
{
    partial class App : Application
    {
        /// <summary>
        /// Various program settings. To ensure that an application crash or a power loss does not result in lost settings,
        /// one of the Save methods should be invoked every time changes are made; this is not automatic.
        /// </summary>
        public static Settings Settings;

        /// <summary>Contains the current UI translation.</summary>
        public static Translation Translation = new Translation();

        /// <summary>
        /// Lists all the possible sources of extra properties, sorted and in an observable fashion. This is kept up-to-date
        /// by the <see cref="MainWindow"/> and used in data binding by the <see cref="DataSourceEditor"/>.
        /// </summary>
        public static ObservableSortedList<DataSourceInfo> DataSources = new ObservableSortedList<DataSourceInfo>(
            items: new DataSourceInfo[] { new DataSourceTierArabic(), new DataSourceTierRoman() },
            comparer: CustomComparer<DataSourceInfo>.By(ds => ds is DataSourceTierArabic ? 0 : ds is DataSourceTierRoman ? 1 : 2)
                .ThenBy(ds => ds.Name).ThenBy(ds => ds.Author));

        /// <summary>
        /// Screen resolution at program start time, relative to the WPF's 96.0 ppi. Windows 7 won't allow this to be changed
        /// without a log-off, so it's OK to read this once at start up and assume it doesn't change.
        /// </summary>
        public static double DpiScaleX, DpiScaleY;

        /// <summary>A list of info classes for each layer type defined in this assembly. Initialised once at startup.</summary>
        public static IList<TypeInfo<LayerBase>> LayerTypes;
        /// <summary>A list of info classes for each effect type defined in this assembly. Initialised once at startup.</summary>
        public static IList<TypeInfo<EffectBase>> EffectTypes;

        [STAThread]
        static int Main(string[] args)
        {
            // Configure Classify. This goes before the post-build check because it depends on it
            Classify.DefaultOptions = new ClassifyOptions()
                .AddTypeOptions(typeof(W.Color), new ColorTypeOptions())
                .AddTypeOptions(typeof(D.Color), new ColorTypeOptions())
                .AddTypeOptions(typeof(Filename), new filenameTypeOptions())
                .AddTypeOptions(typeof(ObservableCollection<LayerBase>), new ListLayerBaseOptions())
                .AddTypeOptions(typeof(ObservableCollection<EffectBase>), new ListEffectBaseOptions());

            if (args.Length == 2 && args[0] == "--post-build-check")
                return RT.Util.Ut.RunPostBuildChecks(args[1], Assembly.GetExecutingAssembly());

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

#if DEBUG
            CompositePath.Tests();
#endif

            Thread.CurrentThread.Name = "Main";
#if !DEBUG
            AppDomain.CurrentDomain.UnhandledException += (_, ea) =>
            {
                bool copy = DlgMessage.ShowError(App.Translation.Error.ExceptionGlobal,
                    App.Translation.Error.ErrorToClipboard_Copy, App.Translation.Error.ErrorToClipboard_OK) == 0;
                if (copy)
                    if (Ut.ClipboardSet(Ut.ExceptionToDebugString(ea.ExceptionObject)))
                        DlgMessage.ShowInfo(App.Translation.Error.ErrorToClipboard_Copied);
            };
#else
            var dummy = App.Translation.Error.ExceptionGlobal; // to keep Lingo happy that the string is used
            var dummy2 = new StringBuilder(); // to keep the "using" clause used
#endif

            // Find all the layer and effect types in the assembly (required before settings are loaded)
            App.LayerTypes = findTypes<LayerBase>("layer");
            App.EffectTypes = findTypes<EffectBase>("effect");

            // Load all settings
            SettingsUtil.LoadSettings(out App.Settings);
            UpdateSettingsIfNecessary();

            // Guess the language if the OS language has changed (or this is the first run)
            var osLingo = Ut.GetOsLanguage();
            if (App.Settings.OsLingo != osLingo)
                App.Settings.OsLingo = App.Settings.Lingo = osLingo;
            // Load translation
            App.Translation = Lingo.LoadTranslationOrDefault<Translation>("TankIconMaker", ref App.Settings.Lingo);

            // Run the UI
            var app = new App();
            app.InitializeComponent();
            app.Run();

            // Save settings upon exit, even though they should be saved on every change anyway
            App.Settings.SaveQuiet();

            return 0;
        }

        private static IList<TypeInfo<T>> findTypes<T>(string name) where T : IHasTypeNameDescription
        {
            var infos = new List<TypeInfo<T>>();
            foreach (var type in Assembly.GetEntryAssembly().GetTypes().Where(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var constructor = type.GetConstructor(new Type[0]);
                if (constructor == null)
                {
                    // (the error message will only be seen by maker developers, so it's ok that it's shown before any UI appears)
                    DlgMessage.ShowWarning("Ignored {1} type \"{0}\" because it does not have a public parameterless constructor.".Fmt(type, name));
                }
                else
                {
                    infos.Add(new TypeInfo<T>
                    {
                        Type = type,
                        Constructor = () => (T) constructor.Invoke(new object[0]),
                        Name = type.Name,
                        Description = type.FullName,
                    });
                }
            }
            infos.Sort(CustomComparer<TypeInfo<T>>.By(ti => ti.Name));
            return infos.AsReadOnly();
        }

        private static void UpdateSettingsIfNecessary()
        {
            var curVersion = Assembly.GetExecutingAssembly().GetName().Version.Major;
            if (App.Settings.SavedByVersion != curVersion)
            {
                try
                {
                    var origName = SettingsUtil.GetAttribute<Settings>().GetFileName();
                    File.Copy(origName, Path.Combine(
                        Path.GetDirectoryName(origName),
                        Path.GetFileNameWithoutExtension(origName) + ".v" +
                        App.Settings.SavedByVersion.ToString().PadLeft(3, '0') + Path.GetExtension(origName)));
                }
                catch
                {
                }

                var migrator = new Migrator();
                migrator.MigrateToVersion(App.Settings, App.Settings.SavedByVersion, curVersion);
            }
        }

        private static void PostBuildCheck(IPostBuildReporter rep)
        {
            Lingo.PostBuildStep<Translation>(rep, Assembly.GetExecutingAssembly());
            Classify.PostBuildStep<Settings>(rep);
            Classify.PostBuildStep<Style>(rep);
            Classify.PostBuildStep<GameVersionConfig>(rep);
        }
    }
}
