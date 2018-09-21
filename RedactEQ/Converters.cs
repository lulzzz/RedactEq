using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace WPFTools
{

    public class EnumToBooleanConverter : IValueConverter
    {
        // XAML Usage:
//        <StackPanel>
//          <StackPanel.Resources>          
//              <local:EnumToBooleanConverter x:Key="EnumToBooleanConverter" />          
//          </StackPanel.Resources>
//          <RadioButton IsChecked = "{Binding Path=YourEnumProperty, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter={x:Static local:YourEnumType.Enum1}}" />
//          < RadioButton IsChecked="{Binding Path=YourEnumProperty, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter={x:Static local:YourEnumType.Enum2}}" />
//       </StackPanel>


        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null)
            {
                return false; // or return parameter.Equals(YourEnumType.SomeDefaultValue);
            }
            return value.Equals(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value.Equals(true) ? parameter : Binding.DoNothing;
        }
    }
}
