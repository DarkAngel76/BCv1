namespace BCv1
{
    static class DisplayManager
    {
        private static Display display;

        public static void Init()
        {
            display = new BCv1.Display();
            display.Init(true);
            Update();
        }

        static void Update()
        {
            display.ClearDisplayBuf();

            DrawBody();

            display.DisplayUpdate();
        }

        static void DrawBody()
        {
            // Row 0, and image
            display.WriteImageDisplayBuf(DisplayImages.Connected, 0, 0);

            // Row 1 - 3
            display.WriteLineDisplayBuf("Hello", 0, 1);
            display.WriteImageDisplayBuf(DisplayImages.ClockUp, 0, 2);
            display.WriteImageDisplayBuf(DisplayImages.ClockDown, 0, 3);
        }     

    }
}
