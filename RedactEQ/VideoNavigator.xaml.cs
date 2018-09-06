using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VideoTools
{
    /// <summary>
    /// Interaction logic for RangeSlider.xaml
    /// </summary>
    public partial class VideoNavigator : UserControl
    {
        bool m_dragRange_MouseDown;
        double m_dragRange_startX;
        //int m_count;

        public delegate void RangeChangedEventHandler(object sender, RangeSliderEventArgs e);
        public event RangeChangedEventHandler RangeChanged;
        protected virtual void OnRangedChanged(RangeSliderEventArgs e)
        {
            if (RangeChanged != null)
                RangeChanged(this, e);
        }

        public VideoNavigator()
        {
            this.InitializeComponent();
            this.LayoutUpdated += new EventHandler(RangeSlider_LayoutUpdated);
            //m_count = 0;
        }

        void RangeSlider_LayoutUpdated(object sender, EventArgs e)
        {
            SetProgressBorder();
            SetLowerValueVisibility();
        }

        private void SetProgressBorder()
        {
            double lowerPoint = (this.ActualWidth * (LowerValue - Minimum)) / (Maximum - Minimum);
            double upperPoint = (this.ActualWidth * (UpperValue - Minimum)) / (Maximum - Minimum);
            upperPoint = this.ActualWidth - upperPoint;
            progressBorder.Margin = new Thickness(lowerPoint, 0, upperPoint, 0);
        }

        public void SetLowerValueVisibility()
        {
            if (DisableLowerValue)
            {
                LowerSlider.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                LowerSlider.Visibility = System.Windows.Visibility.Visible;
            }
        }

        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public double LowerValue
        {
            get { return (double)GetValue(LowerValueProperty); }
            set { SetValue(LowerValueProperty, value); }
        }

        public double UpperValue
        {
            get { return (double)GetValue(UpperValueProperty); }
            set { SetValue(UpperValueProperty, value); }
        }

        public double CurrentValue
        {
            get { return (double)GetValue(CurrentValueProperty); }
            set { SetValue(CurrentValueProperty, value); }
        }

        public bool DisableLowerValue
        {
            get { return (bool)GetValue(DisableLowerValueProperty); }
            set { SetValue(DisableLowerValueProperty, value); }
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(0d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty LowerValueProperty =
            DependencyProperty.Register("LowerValue", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(0d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty UpperValueProperty =
            DependencyProperty.Register("UpperValue", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(100d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(100d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty CurrentValueProperty =
            DependencyProperty.Register("CurrentValue", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(0d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty DisableLowerValueProperty =
            DependencyProperty.Register("DisableLowerValue", typeof(bool), typeof(VideoNavigator), new UIPropertyMetadata(false, new PropertyChangedCallback(DisabledLowerValueChanged)));

        private static void DisabledLowerValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoNavigator slider = (VideoNavigator)d;
            slider.SetLowerValueVisibility();
        }

        private static void PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoNavigator slider = (VideoNavigator)d;
            if (e.Property == VideoNavigator.LowerValueProperty)
            {
                slider.UpperSlider.Value = Math.Max(slider.UpperSlider.Value, slider.LowerSlider.Value);
                slider.CurrentSlider.Value = Math.Max(slider.LowerSlider.Value, slider.CurrentSlider.Value);
            }
            else if (e.Property == VideoNavigator.UpperValueProperty)
            {
                slider.LowerSlider.Value = Math.Min(slider.UpperSlider.Value, slider.LowerSlider.Value);
                slider.CurrentSlider.Value = Math.Min(slider.UpperSlider.Value, slider.CurrentSlider.Value);                
            }
            else if(e.Property == VideoNavigator.CurrentValueProperty)
            {
                slider.UpperSlider.Value = Math.Max(slider.UpperSlider.Value, slider.CurrentSlider.Value);
                slider.LowerSlider.Value = Math.Min(slider.LowerSlider.Value, slider.CurrentSlider.Value);
            }
            slider.SetProgressBorder();
        }

        private void progressBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            m_dragRange_MouseDown = true;
            m_dragRange_startX = e.GetPosition(this).X;
        }

        private void progressBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            m_dragRange_MouseDown = false;
        }

        private void progressBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (m_dragRange_MouseDown)
            {
                //m_count++;
                double parentPixelWidth = LayoutRoot.ActualWidth;
                double currentPos = e.GetPosition(this).X;
                double pixelDelta = currentPos - m_dragRange_startX;
                double percentMove = pixelDelta / parentPixelWidth;

                double rangeDelta = percentMove * (Maximum - Minimum);

                double newLowerValue = LowerValue + rangeDelta;
                double newUpperValue = UpperValue + rangeDelta;

                if (newLowerValue >= Minimum && newUpperValue <= Maximum)
                {
                    LowerValue = newLowerValue;
                    UpperValue = newUpperValue;
                    m_dragRange_startX = currentPos;
                }
            }

            e.Handled = true;
        }

        private void LayoutRoot_MouseMove(object sender, MouseEventArgs e)
        {
            progressBorder_MouseMove(sender, e);
        }

        private void LayoutRoot_MouseLeave(object sender, MouseEventArgs e)
        {
            m_dragRange_MouseDown = false;
        }

        private void LowerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RangeSliderEventArgs e1 = new RangeSliderEventArgs(LowerSlider.Value, UpperSlider.Value, CurrentSlider.Value);
            OnRangedChanged(e1);
        }

        private void UpperSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RangeSliderEventArgs e1 = new RangeSliderEventArgs(LowerSlider.Value, UpperSlider.Value, CurrentSlider.Value);
            OnRangedChanged(e1);
        }

        private void CurrentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            RangeSliderEventArgs e1 = new RangeSliderEventArgs(LowerSlider.Value, UpperSlider.Value, CurrentSlider.Value);
            OnRangedChanged(e1);
        }

    }





    public class RangeSliderEventArgs : EventArgs
    {
        private readonly double min;
        private readonly double max;
        private readonly double cur;

        // Constructor
        public RangeSliderEventArgs(double min, double max, double current)
        {
            this.min = min;
            this.max = max;
            this.cur = current;
        }

        // Properties
        public double Minimum
        {
            get { return this.min; }
        }

        public double Maximum
        {
            get { return this.max; }
        }

        public double Current
        {
            get { return this.cur; }
        }
    }



}