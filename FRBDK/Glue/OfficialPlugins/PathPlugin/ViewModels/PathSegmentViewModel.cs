﻿using FlatRedBall.Glue.MVVM;
using FlatRedBall.Math.Paths;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace OfficialPlugins.PathPlugin.ViewModels
{
    public class PathSegmentViewModel : ViewModel
    {
        public event Action<PathSegmentViewModel> CloseClicked;

        static List<SegmentType> availableValuesStatic = new List<SegmentType>()
        {
            SegmentType.Line,
            SegmentType.Arc
        };

        public IEnumerable<SegmentType> AvailableSegmentTypes => availableValuesStatic;

        public SegmentType SegmentType
        {
            get => Get<SegmentType>();
            set => Set(value);
        }

        [DependsOn(nameof(SegmentType))]
        public Visibility AngleVisibility => (SegmentType == SegmentType.Arc).ToVisibility();

        public float X
        {
            get => Get<float>();
            set => Set(value);
        }

        public float Y
        {
            get => Get<float>();
            set => Set(value);
        }

        public float Angle
        {
            get => Get<float>();
            set => Set(value);
        }

        public void HandleCloseClicked() => CloseClicked(this);
    }
}
