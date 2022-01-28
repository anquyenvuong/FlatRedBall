﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OfficialPlugins.Compiler.Models
{
    public class CompilerSettingsModel
    {
        public bool GenerateGlueControlManagerCode { get; set; }

        public bool EmbedGameInGameTab { get; set; }

        public bool RestartScreenOnLevelContentChange { get; set; }
        public int PortNumber { get; set; } = 8021;
        public bool ShowScreenBoundsWhenViewingEntities { get; set; } 
        public decimal GridSize { get; set; } = 32;

        public void SetDefaults()
        {
            EmbedGameInGameTab = true;
            ShowScreenBoundsWhenViewingEntities = true;
            RestartScreenOnLevelContentChange = true;
        }
    }
}
