namespace Compositor.Imaging;

// Internal diagnostic barriers; production saves do not install a callback.
internal enum SaveCheckpoint { AssetWritten, StagingValidated, BeforePublish, Published }
