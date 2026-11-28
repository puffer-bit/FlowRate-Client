// Adapted from FluentAvalonia.ProgressRing (https://github.com/ymg2006/FluentAvalonia.ProgressRing),
// Copyright (c) 2022 ymg2006, MIT license. See THIRD-PARTY-NOTICES.md.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Client.Avalonia.Styles.Animation;

namespace Client.Avalonia.Controls.LoadingIndicators
{
    /// <summary>
    /// A control used to indicate the progress of an operation.
    /// </summary>
    [PseudoClasses(":preserveaspect", ":indeterminate", ShownState.PseudoClass)]
    public class ProgressRing : RangeBase
    {
        public static readonly StyledProperty<bool> IsIndeterminateProperty =
            ProgressBar.IsIndeterminateProperty.AddOwner<ProgressRing>();

        public static readonly StyledProperty<bool> PreserveAspectProperty =
            AvaloniaProperty.Register<ProgressRing, bool>(nameof(PreserveAspect), true);

        public static readonly StyledProperty<double> ValueAngleProperty =
            AvaloniaProperty.Register<ProgressRing, double>(nameof(ValueAngle), 0);

        public static readonly StyledProperty<double> StartAngleProperty =
            AvaloniaProperty.Register<ProgressRing, double>(nameof(StartAngle), 0);

        public static readonly StyledProperty<double> EndAngleProperty =
            AvaloniaProperty.Register<ProgressRing, double>(nameof(EndAngle), 360);

        public ProgressRing()
        {
            UpdatePseudoClasses(IsIndeterminate, PreserveAspect);
            // The template animates only under :shown, so a hidden ring costs nothing
            ShownState.SetTrack(this, true);
        }

        public bool IsIndeterminate
        {
            get => GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        public bool PreserveAspect
        {
            get => GetValue(PreserveAspectProperty);
            set => SetValue(PreserveAspectProperty, value);
        }

        public double ValueAngle
        {
            get => GetValue(ValueAngleProperty);
            private set => SetValue(ValueAngleProperty, value);
        }

        public double StartAngle
        {
            get => GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
        }

        public double EndAngle
        {
            get => GetValue(EndAngleProperty);
            set => SetValue(EndAngleProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsIndeterminateProperty || change.Property == PreserveAspectProperty)
            {
                UpdatePseudoClasses(IsIndeterminate, PreserveAspect);
            }
            else if (change.Property == MinimumProperty || change.Property == MaximumProperty
                     || change.Property == ValueProperty || change.Property == StartAngleProperty
                     || change.Property == EndAngleProperty)
            {
                UpdateValueAngle();
            }
        }

        private void UpdatePseudoClasses(
            bool? isIndeterminate,
            bool? preserveAspect)
        {
            if (isIndeterminate.HasValue)
            {
                PseudoClasses.Set(":indeterminate", isIndeterminate.Value);
            }

            if (preserveAspect.HasValue)
            {
                PseudoClasses.Set(":preserveaspect", preserveAspect.Value);
            }
        }

        private void UpdateValueAngle()
        {
            var range = Maximum - Minimum;
            ValueAngle = range > 0 ? (Value - Minimum) * (EndAngle - StartAngle) / range : 0;
        }
    }
}
