using System;
using System.IO;
using XmlSettings;

namespace WPFTools
{
    public class GlobalVars
    {
        const string settingsFilename = "settings.xml";

        public static int decodeWidth;

        public static int decodeHeight;

        public static int encodeWidth;

        public static int encodeHeight;

        public static string dnn_modelFile;

        public static string dnn_catalogFile;


        private static void SetDefaults()
        {
            decodeWidth = 640;
            decodeHeight = 480;
            encodeWidth = 0;
            encodeHeight = 0;
            dnn_modelFile = "frozen_inference_graph.pb";
            dnn_catalogFile = "face_label_map.pbtxt";
        }


        public static void LoadSettings()
        {
            if(!File.Exists(settingsFilename))
            {
                SetDefaults();
                SaveSettings();
            }

            Settings settings = new Settings(settingsFilename);

            decodeWidth = Convert.ToInt32(settings.GetValue("MAIN", "DecodeWidth"));
            decodeHeight = Convert.ToInt32(settings.GetValue("MAIN", "DecodeHeight"));
            encodeWidth = Convert.ToInt32(settings.GetValue("MAIN", "EncodeWidth"));
            encodeHeight = Convert.ToInt32(settings.GetValue("MAIN", "EncodeHeight"));
            dnn_modelFile = settings.GetValue("MAIN", "DNN_ModelFile");
            dnn_catalogFile = settings.GetValue("MAIN", "DNN_CatalogFile");
        }

        public static void SaveSettings()
        {
            Settings settings = new Settings(settingsFilename);

            settings.SetValue("MAIN", "DecodeWidth", decodeWidth.ToString());
            settings.SetValue("MAIN", "DecodeHeight", decodeHeight.ToString());
            settings.SetValue("MAIN", "EncodeWidth", encodeWidth.ToString());
            settings.SetValue("MAIN", "EncodeHeight", encodeHeight.ToString());
            settings.SetValue("MAIN", "DNN_ModelFile", dnn_modelFile);
            settings.SetValue("MAIN", "DNN_CatalogFile", dnn_catalogFile);
        }
    }
}
