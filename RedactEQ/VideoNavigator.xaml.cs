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

        private bool dragStarted = false;

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
            //this.LayoutUpdated += new EventHandler(RangeSlider_LayoutUpdated);
            //m_count = 0;
            TickPositions = new DoubleCollection() { 0 };
            MarkerPositions = new DoubleCollection() { 0 };
        }

        void RangeSlider_LayoutUpdated(object sender, EventArgs e)
        {
           
        }

 
        public void SetTickPositions(DoubleCollection ticks)
        { 
            TickPositions = ticks;
        }

        public void SetMarkerPositions(DoubleCollection markers)
        {
            MarkerPositions = markers;
        }



        public void SetTickPositions()
        {
            CurrentSlider.Ticks = TickPositions;
        }

        public void SetMarkerPositions()
        {
            CurrentSlider.Ticks = MarkerPositions;
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

  
        public double CurrentValue
        {
            get { return (double)GetValue(CurrentValueProperty); }
            set { SetValue(CurrentValueProperty, value); }
        }

  
        public DoubleCollection TickPositions
        {
            get { return (DoubleCollection)GetValue(TickPositionsProperty); }
            set { SetValue(TickPositionsProperty, value); }
        }

        public DoubleCollection MarkerPositions
        {
            get { return (DoubleCollection)GetValue(MarkerPositionsProperty); }
            set { SetValue(MarkerPositionsProperty, value); }
        }


        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(0d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(100d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty CurrentValueProperty =
            DependencyProperty.Register("CurrentValue", typeof(double), typeof(VideoNavigator), new UIPropertyMetadata(0d, new PropertyChangedCallback(PropertyChanged)));

        public static readonly DependencyProperty TickPositionsProperty =
          DependencyProperty.Register("TickPositions", typeof(DoubleCollection), typeof(VideoNavigator), new UIPropertyMetadata(null, new PropertyChangedCallback(TickPositionsValueChanged)));

        public static readonly DependencyProperty MarkerPositionsProperty =
          DependencyProperty.Register("MarkerPositions", typeof(DoubleCollection), typeof(VideoNavigator), new UIPropertyMetadata(null, new PropertyChangedCallback(MarkerPositionsValueChanged)));


        private static void TickPositionsValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoNavigator slider = (VideoNavigator)d;
            slider.SetTickPositions();
        }

        private static void MarkerPositionsValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoNavigator slider = (VideoNavigator)d;
            slider.SetMarkerPositions();
        }

        private static void PropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VideoNavigator slider = (VideoNavigator)d;
            if (e.Property == VideoNavigator.CurrentValueProperty)
            {
    
            }         
        }

      
   
        private void Thumb_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            dragStarted = true;
        }

        private void Thumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (dragStarted)
            {
                RangeSliderEventArgs e1 = new RangeSliderEventArgs(Minimum, Maximum, CurrentValue);
                OnRangedChanged(e1);
                dragStarted = false;
            }            
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