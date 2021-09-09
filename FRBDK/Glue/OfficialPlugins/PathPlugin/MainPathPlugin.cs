﻿using FlatRedBall.Glue.FormHelpers.PropertyGrids;
using FlatRedBall.Glue.Plugins;
using FlatRedBall.Glue.Plugins.Interfaces;
using FlatRedBall.Glue.SaveClasses;
using OfficialPlugins.PathPlugin.Managers;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace OfficialPlugins.PathPlugin
{
    [Export(typeof(PluginBase))]
    public class MainPathPlugin : PluginBase
    {
        public override string FriendlyName => "Path Plugin";

        public override Version Version => new Version(1, 0);

        public override bool ShutDown(PluginShutDownReason shutDownReason)
        {
            return true;
        }

        public override void StartUp()
        {
            AddAssetTypeInfo(AssetTypeInfoManager.PathAssetTypeInfo);

        }

    }
}