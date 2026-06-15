using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace YAHAA.Setup
{
    /// <summary>
    /// Coordinates the multi-step setup wizard: holds the data collected across steps and
    /// drives the host <see cref="Frame"/> with horizontal slide transitions, mimicking the
    /// Home Assistant mobile onboarding flow.
    /// </summary>
    public sealed class SetupFlow
    {
        private readonly Frame _frame;
        private readonly List<Type> _steps;
        private int _index = -1;

        /// <summary>Raised whenever the active step changes. Args are (currentIndex, totalSteps).</summary>
        public event Action<int, int>? StepChanged;

        // Data gathered while the user moves through the wizard.
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string? LocationName { get; set; }
        public string? Version { get; set; }

        public SetupFlow(Frame frame, IReadOnlyList<Type> steps)
        {
            _frame = frame;
            _steps = new List<Type>(steps);
        }

        public int StepCount => _steps.Count;
        public int CurrentIndex => _index;

        public void Start()
        {
            _index = 0;
            _frame.Navigate(_steps[_index], this, new EntranceNavigationTransitionInfo());
            StepChanged?.Invoke(_index, _steps.Count);
        }

        public void Next()
        {
            if (_index >= _steps.Count - 1) return;
            _index++;
            _frame.Navigate(_steps[_index], this,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
            StepChanged?.Invoke(_index, _steps.Count);
        }

        public void Back()
        {
            if (_index <= 0) return;
            _index--;
            _frame.Navigate(_steps[_index], this,
                new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromLeft });
            StepChanged?.Invoke(_index, _steps.Count);
        }
    }
}
