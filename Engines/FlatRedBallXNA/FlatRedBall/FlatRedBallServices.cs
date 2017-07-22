#if FRB_MDX || XNA3
#define SUPPORTS_FRB_DRAWN_GUI
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

using FlatRedBall.Graphics;
using FlatRedBall.Input;
using FlatRedBall.Instructions;

using FlatRedBall.IO;

#if !WINDOWS_8
using FlatRedBall.Content.AI.Pathfinding;
using FlatRedBall.Content.Math.Geometry;
using FlatRedBall.Content.Saves;
#endif

using FlatRedBall.AI.Pathfinding;
using FlatRedBall.Math.Geometry;


using System.IO;

using FlatRedBall.Graphics.Animation;
using FlatRedBall.Gui;

using FlatRedBall.Content.AnimationChain;
using FileManager = FlatRedBall.IO.FileManager;
using ShapeManager = FlatRedBall.Math.Geometry.ShapeManager;

using FlatRedBall.Content;
using FlatRedBall.Graphics.Particle;
using FlatRedBall.Content.Particle;
using FlatRedBall.Math;
using FlatRedBall.Content.Polygon;
using FlatRedBall.IO.Csv;

using Microsoft.Xna.Framework;
#if FRB_MDX
using FlatRedBall.Gui;
using GraphicsDevice = Microsoft.DirectX.Direct3D.Device;
using Microsoft.DirectX.Direct3D;

#else

using Microsoft.Xna.Framework.Graphics;

using Microsoft.Xna.Framework.Content;


using Renderer = FlatRedBall.Graphics.Renderer;
using Effect = Microsoft.Xna.Framework.Graphics.Effect;
using InstructionManager = FlatRedBall.Instructions.InstructionManager;


using Texture2D = Microsoft.Xna.Framework.Graphics.Texture2D;
#if !MONOGAME
using Model = Microsoft.Xna.Framework.Graphics.Model;
using FlatRedBall.Instructions.ScriptedAnimations;
#endif
using ContentManager = Microsoft.Xna.Framework.Content.ContentManager;

using FlatRedBall.Audio;

using System.Text.RegularExpressions;
using FlatRedBall.Content.Instructions;


#endif
using FlatRedBall.Graphics.Texture;
using FlatRedBall.Math.Splines;
using FlatRedBall.Content.Math.Splines;
using FlatRedBall.Performance.Measurement;
using FlatRedBall.Managers;

#if FRB_MDX

#elif XNA4 || MONOGAME
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
#else
    using Color = Microsoft.Xna.Framework.Graphics.Color;
    using Rectangle = Microsoft.Xna.Framework.Rectangle;
#endif

namespace FlatRedBall
{
    #region Profiling enums and structs
#if PROFILE
    public enum ContentLoadDetail
    {
        Cached,
        HddFromContentPipeline,
        HddFromFile
    }

    public struct ContentLoadHistory
    {
        public double Time;
        public string Type;
        public string ContentName;
        public ContentLoadDetail ContentLoadDetail;

        public string SpecialEvent;

        public ContentLoadHistory(double time, string type, string contentName, ContentLoadDetail contentLoadDetail)
        {
            Time = time;
            Type = type;
            ContentName = contentName;
            
            ContentLoadDetail = contentLoadDetail;
            SpecialEvent = null;
        }
    }
#endif
    #endregion

    #region XML Docs
    /// <summary>
    /// Container class implements the IServiceProvider interface. This is used
    /// to pass shared services between different components, for instance the
    /// ContentManager uses it to locate the IGraphicsDeviceService implementation.
    /// </summary>
    #endregion
    public class ServiceContainer : IServiceProvider
    {
        #region Fields

        Dictionary<Type, object> services = new Dictionary<Type, object>();

        #endregion

        #region Methods

        public void AddService<T>(T service)
        {
            services.Add(typeof(T), service);
        }

        public object GetService(Type serviceType)
        {
            object service;

            services.TryGetValue(serviceType, out service);

            return service;
        }

        #endregion
    }

    public static partial class FlatRedBallServices
    {
        #region Fields

        static List<IManager> mManagers = new List<IManager>();

        static IServiceProvider mServices;

        static IntPtr mWindowHandle;

        static int mPrimaryThreadId;

#if !MONOGAME
        static System.Windows.Forms.Control mOwner;
#endif

        static internal Dictionary<string, FlatRedBall.Content.ContentManager> mContentManagers; // keep this null, it's used by Initialization to know if the engine needs pre-initialization
        static List<FlatRedBall.Content.ContentManager> mContentManagersWaitingToBeDestroyed = new List<Content.ContentManager>();
        static Game mGame = null;
        static object mSuspendLockObject = new object();

        static internal GraphicsDeviceManager mGraphics = null;
        static internal GraphicsDevice mGraphicsDevice;

#if !MONOGAME
        // Content Management
        internal static ResourceContentManager mResourceContentManager;
#endif
        
        static FlatRedBall.Utilities.GameRandom mRandom = new FlatRedBall.Utilities.GameRandom();

        // Graphics options
        static internal int mClientWidth;
        static internal int mClientHeight;
        static GraphicsOptions mGraphicsOptions;

        static internal bool mIsInitialized = false;
        static internal bool mIsCommandLine = false;
        static bool mIsSuspended = false;

        static List<Type> mTypesThatCanBeLoaded = new List<Type>();

        /// <remarks>
        /// This is *NOT* secure, keys in code can easily be obtained by disassembling the game.
        /// </remarks>
        static string mEncryptionKey = String.Empty;

        private static Texture2D _textureToDraw = null;
        private static SpriteBatch _loadingScreenSpriteBatch = null;
        private static Rectangle _sourceRect;

        #endregion

        #region Properties

        public static bool IsInitialized
        {
            get { return mIsInitialized; }
        }

        public static FlatRedBall.Utilities.GameRandom Random
        {
            get { return mRandom; }
            set
            {
#if DEBUG
                if (value == null)
                    throw new Exception("Cannot set Random as Null");
#endif
                mRandom = value;
            }
        }

        public static GraphicsDevice GraphicsDevice
        {
#if FRB_MDX
            get { return Renderer.GraphicsDevice; }
#else
            get { return Renderer.Graphics.GraphicsDevice; }
#endif
        }

        public static bool IsWindowsCursorVisible
        {
            get
            {
#if FRB_MDX
                return mWindowsCursorVisible;
#else
                return (mGame == null) ? true : mGame.IsMouseVisible;
#endif
            }
            set
            {
#if FRB_MDX
                if (mWindowsCursorVisible == true && value == false)
                {
                    // It is visisble, but the user wants to hide it.
                    System.Windows.Forms.Cursor.Hide();
                    mWindowsCursorVisible = false;

                    InputManager.Mouse.ReacquireExclusive();
                }
                else if (mWindowsCursorVisible == false && value == true)
                {
                    // It is invisible and the user wants to show it.
                    System.Windows.Forms.Cursor.Show();
                    mWindowsCursorVisible = true;

                    InputManager.Mouse.ReacquireNonExclusive();
                }
#else
                if (mGame != null) mGame.IsMouseVisible = value;
#endif

            }
        }

#if !XBOX360 && !WINDOWS_PHONE && !MONOGAME
        public static System.Windows.Forms.Control Owner
        {
            get { return mOwner; }
        }
#endif
        public static Game Game
        {
            get
            {
                return mGame;
            }
        }
#if FRB_XNA


        public static IntPtr WindowHandle
        {
            get
            {
                if (mGame != null) return mGame.Window.Handle;
                else return mWindowHandle;
            }
        }


#endif
        public static GraphicsOptions GraphicsOptions
        {
            get { return mGraphicsOptions; }
        }

        public static int ClientWidth
        {
            get { return mClientWidth; }
        }

        public static int ClientHeight
        {
            get { return mClientHeight; }
        }

        public static string GlobalContentManager
        {
            get { return "Global"; }
        }

        /// <summary>
        /// Salt value that is combined with the EncryptionKey string for generating encryption keys
        /// </summary>
        public static string EncryptionSaltValue
        {
            get { return "FRBEncryptionKeySalt"; }
        }

        /// <summary>
        /// Password to use for decrypting files (set this to the appropriate value before attempting to load any CSV files that were encrypted in the content pipeline)
        /// </summary>
        public static string EncryptionKey
        {
            get { return mEncryptionKey; }
            set { mEncryptionKey = value; }
        }

        public static IEnumerable<FlatRedBall.Content.ContentManager> ContentManagers
        {
            get
            {
                return mContentManagers.Values;
            }
        }

#if XBOX360
        public static bool IgnoreExtensionsWhenLoadingContent
        {
            get { return mIgnoreExtensionsWhenLoadingContent; }
            set { mIgnoreExtensionsWhenLoadingContent = value; }
        }
#endif
        #endregion

        #region Events
        
        public static event EventHandler Suspending;
        public static event EventHandler Unsuspending;

        #endregion

        #region Event Methods

        private static bool mWindowResizing = false;

        private static void Window_ClientSizeChanged(object sender, EventArgs e)
        {
            if (!mWindowResizing)
            {
                mWindowResizing = true;


                if (mGame != null)
                {
                    mClientHeight = mGame.Window.ClientBounds.Height;
                    mClientWidth = mGame.Window.ClientBounds.Width;

                    // If we're setting the mClientWith and mClientHeight, we need
                    // to adjust the cameras:
                    // Update cameras
                    foreach (Camera camera in SpriteManager.Cameras)
                    {
                        camera.UpdateOnResize();
                    }
                }
                else
                {
#if XBOX360 || WINDOWS_PHONE || MONOGAME
                    throw new NotSupportedException("Game required on the this platform");
#else
                    System.Windows.Forms.Control window = System.Windows.Forms.Form.FromHandle(mWindowHandle);
                    mClientHeight = window.Height;
                    mClientWidth = window.Width;
#endif
                    foreach (Camera camera in SpriteManager.Cameras)
                    {
                        camera.UpdateOnResize();
                    }
                }


                if (mClientHeight != 0 && mClientWidth != 0)
                {

                    mGraphicsOptions.SuspendDeviceReset();

                    mGraphicsOptions.ResolutionWidth = mClientWidth;
                    mGraphicsOptions.ResolutionHeight = mClientHeight;
                    // Resetting the device crashes W8, but we want the 
                    // values to be updated so we set the resolution values
                    // above
#if !WINDOWS_8
                    mGraphicsOptions.ResumeDeviceReset();

#if WINDOWS 
                    FlatRedBallServices.GraphicsOptions.CallSizeOrOrientationChanged();
#endif

                    mGraphicsOptions.ResetDevice();
#endif
                }
                mWindowResizing = false;
            }
        }

        private static void UpdateToWindowSize(object sender, EventArgs e)
        {
            mGraphicsOptions.SuspendDeviceReset();

            // Vic says:
            // This code dupliates what's in Window_ClientSizeChanged.  I'm not sure why,
            // but if this function is removed, then the game window resizes itself back to
            // the old size instead of to the new one.  Keeping this in keeps resizing working
            // correctly.  

            mGraphicsOptions.ResolutionWidth = mGame.Window.ClientBounds.Width;
            mGraphicsOptions.ResolutionHeight = mGame.Window.ClientBounds.Height;

            mGraphicsOptions.ResumeDeviceReset();

    #if WINDOWS
            FlatRedBallServices.GraphicsOptions.CallSizeOrOrientationChanged();
    #endif
            //mGraphicsOptions.ResumeDeviceReset();
        }

        #endregion

        #region Methods

        #region Constructor/Initialize

        private static void PreInitialization()
        {
            throw new NotSupportedException();
        }

        private static void PreInitialization(Game game, GraphicsDeviceManager graphics)
        {
            if (mContentManagers == null)
            {
                // on iOS the title container must first be accessed on the
                // primary thread:
#if IOS
            try
            {
                Microsoft.Xna.Framework.TitleContainer.OpenStream("asdf_qwer__");
            }
            catch
            {
                // no biggie
            }
#endif


#if WINDOWS_8 || UWP
                mPrimaryThreadId = Environment.CurrentManagedThreadId;
#else
                mPrimaryThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif

                mContentManagers = new Dictionary<string, FlatRedBall.Content.ContentManager>();

                mGraphicsOptions = new Graphics.GraphicsOptions(game, graphics);

                #region Fill the types that can be loaded

                mTypesThatCanBeLoaded.Add(typeof(Texture2D));
                mTypesThatCanBeLoaded.Add(typeof(Scene));
                mTypesThatCanBeLoaded.Add(typeof(EmitterList));
#if !MONOGAME
                mTypesThatCanBeLoaded.Add(typeof(System.Drawing.Image));
                mTypesThatCanBeLoaded.Add(typeof(BitmapList));
#endif


                mTypesThatCanBeLoaded.Add(typeof(Effect));
                mTypesThatCanBeLoaded.Add(typeof(NodeNetwork));
                mTypesThatCanBeLoaded.Add(typeof(ShapeCollection));
                mTypesThatCanBeLoaded.Add(typeof(PositionedObjectList<Polygon>));
                mTypesThatCanBeLoaded.Add(typeof(AnimationChainList));

                #endregion


            }
        }
#if XNA4  && !MONODROID
        public static void InitializeCommandLine()
        {
            InitializeCommandLine(null);
        }

        #region XML Docs
        /// <summary>
        /// Used to initialize FlatRedBall without rendering anything to the screen
        /// </summary>
        /// <param name="game">The game</param>
        #endregion
        public static void InitializeCommandLine(Game game)
        {
#if WINDOWS_8 || UWP
                mPrimaryThreadId = Environment.CurrentManagedThreadId;
#else
            mPrimaryThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif


            mIsCommandLine = true;

            mContentManagers = new Dictionary<string, FlatRedBall.Content.ContentManager>();

#if !UNIT_TESTS
            if (game == null)
            {
                mServices = new ServiceContainer();
            }
            else
            {

                mServices = game.Services;
                mGame = game;

                mWindowHandle = game.Window.Handle;
            }
#else
            mServices = new ServiceContainer();

#endif

            InstructionManager.Initialize();
            TimeManager.Initialize();
            ShapeManager.Initialize();
            InputManager.Initialize(mWindowHandle);
            SpriteManager.Initialize();


            mIsInitialized = true;
        }
#endif

        #region XML Docs
        /// <summary>
        /// Used to initialize FlatRedBall with a game
        /// </summary>
        /// <param name="game">The game</param>
        /// <param name="graphics">The graphics device manager</param>
        #endregion
        public static void InitializeFlatRedBall(Game game, GraphicsDeviceManager graphics)
        {

            PreInitialization(game, graphics);

            GraphicsOptions graphicsOptions = new GraphicsOptions(game, graphics);


            // Call the base initialization method
            InitializeFlatRedBall(game, graphics, graphicsOptions);
        }

        #region XML Docs
        /// <summary>
        /// Used to initialize FlatRedBall with a game and graphics options
        /// </summary>
        /// <param name="game">The game</param>
        /// <param name="graphics">The graphics device manager</param>
        /// <param name="graphicsOptions">The graphics options to use for this game</param>
        #endregion
        public static void InitializeFlatRedBall(Game game, GraphicsDeviceManager graphics,
            GraphicsOptions graphicsOptions)
        {

            graphics.PreparingDeviceSettings += (object sender, PreparingDeviceSettingsEventArgs args) =>
            {
                args.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            };

        PlatformServices.Initialize();

            PreInitialization(game, graphics);
            mGraphics = graphics;
            mGraphicsDevice = mGraphics.GraphicsDevice;

            mGraphicsOptions = graphicsOptions;

            mClientWidth = mGraphicsOptions.ResolutionWidth;
            mClientHeight = mGraphicsOptions.ResolutionHeight;

#if !WINDOWS_8
            mGraphics.PreferredBackBufferWidth = mClientWidth;
            mGraphics.PreferredBackBufferHeight = mClientHeight;
#endif
            mGraphics.PreferMultiSampling = mGraphicsOptions.UseMultiSampling;

            if (mGraphicsOptions.IsFullScreen != mGraphics.IsFullScreen)
                mGraphics.ToggleFullScreen();

#if !WINDOWS_8
            mGraphics.ApplyChanges();
#endif

            mGraphics.PreparingDeviceSettings += new EventHandler<PreparingDeviceSettingsEventArgs>(graphics_PreparingDeviceSettings);

            mGraphics.DeviceReset += new EventHandler<EventArgs>(graphics_DeviceReset);

            // Monogame Windows 8 doesn't currently ( and maybe never will) support resets, so we need to 
            // handle the resolution changing:
#if WINDOWS_8
            FlatRedBallServices.GraphicsOptions.SizeOrOrientationChanged += HandleSizeOrOrientationChanged;
#endif

            mServices = game.Services;
            mGame = game;

            mWindowHandle = game.Window.Handle;

            // Victor Chelaru
            // March 3, 2016
            // There seems to be
            // some inconsistency
            // on XNA PC with resizing
            // the window. mOwner.Resize
            // is called whenever the user
            // maximizes the window or restores
            // it to windowed, but game.window.ClientSizeChanged
            // is only raised when maximizing but not restoring. I
            // am wondering why we have both, and why can't we just
            // use one of them.


#if WINDOWS
            mOwner =
                System.Windows.Forms.Form.FromHandle(mWindowHandle);


            mOwner.MinimumSize = new System.Drawing.Size(mOwner.MinimumSize.Width, 35);

            mOwner.GotFocus += new EventHandler(mOwner_GotFocus);

#endif

#if WINDOWS

            mOwner.Resize += new EventHandler(Window_ClientSizeChanged);
#else
            game.Window.ClientSizeChanged += new EventHandler<EventArgs>(Window_ClientSizeChanged);
#endif
            // call this *after assignign the events
            mGraphicsOptions.Initialize();

            CommonInitialize(graphics);
            
        }

#if WINDOWS_8

        private static void HandleSizeOrOrientationChanged(object sender, EventArgs e)
        {
            // Windows 8 is different than other engines (currently)
            // because the user can run at a different resolution than
            // the physical display.  In Windows 7 the resolution of the window
            // is the resolution of the game.  In Windows 8 the game always runs
            // in fullscreen.  We only want to set the resolution *if* the user hasn't
            // already set the resolution manually.

            mHandlingReset = true;

            mGraphicsOptions.SuspendDeviceReset();
            if ( mGraphicsOptions.mHasResolutionBeenManuallySet == false)
            {
                mGraphicsOptions.ResolutionWidth = mGame.Window.ClientBounds.Width;
                mGraphicsOptions.ResolutionHeight = mGame.Window.ClientBounds.Height;
            }
            mGraphicsOptions.ResumeDeviceReset();

            // Update client sizes
            mClientWidth = mGraphicsOptions.ResolutionWidth;
            mClientHeight = mGraphicsOptions.ResolutionHeight;

            //Window_ClientSizeChanged(sender, e);

            // Update cameras
            foreach (Camera camera in SpriteManager.Cameras)
            {
                camera.UpdateOnResize();
            }

            mHandlingReset = false;
        }
#endif

        static void mOwner_GotFocus(object sender, EventArgs e)
        {
            InputManager.Mouse.Clear();
        }

            #region XML Docs
        /// <summary>
        /// Used to intialize FlatRedBall without a game object (e.g. in windows forms projects)
        /// </summary>
        /// <param name="graphics">The graphics device service</param>
        /// <param name="windowHandle">The window handle</param>
            #endregion
        public static void InitializeFlatRedBall(IGraphicsDeviceService graphics, IntPtr windowHandle)
        {
            PreInitialization();

            GraphicsOptions graphicsOptions = new GraphicsOptions();

            #region Get Resolution
            graphicsOptions.SuspendDeviceReset();
            graphicsOptions.ResolutionWidth = graphics.GraphicsDevice.PresentationParameters.BackBufferWidth;
            graphicsOptions.ResolutionHeight = graphics.GraphicsDevice.PresentationParameters.BackBufferHeight;
            graphicsOptions.ResumeDeviceReset();
            #endregion

            InitializeFlatRedBall(graphics, windowHandle, graphicsOptions);
        }

            #region XML Docs
        /// <summary>
        /// Used to intialize FlatRedBall without a game object (e.g. in windows forms projects) and with graphics options
        /// </summary>
        /// <param name="graphics">The graphics device service</param>
        /// <param name="windowHandle">The window handle</param>
        /// <param name="graphicsOptions">The graphics options</param>
            #endregion
        public static void InitializeFlatRedBall(IGraphicsDeviceService graphics, IntPtr windowHandle,
            GraphicsOptions graphicsOptions)
        {

            PreInitialization();

            mGraphics = graphics as GraphicsDeviceManager;
            mGraphicsDevice = mGraphics.GraphicsDevice;


            mGraphicsOptions = graphicsOptions;
#if !WINDOWS_PHONE
            mGraphics.PreferredBackBufferWidth = mClientWidth;
            mGraphics.PreferredBackBufferHeight = mClientHeight;
#endif

            mGraphics.PreferMultiSampling = mGraphicsOptions.UseMultiSampling;

            if (mGraphicsOptions.IsFullScreen != mGraphics.IsFullScreen)
                mGraphics.ToggleFullScreen();

            mGraphics.ApplyChanges();

            mGraphics.PreparingDeviceSettings += new EventHandler<PreparingDeviceSettingsEventArgs>(graphics_PreparingDeviceSettings);




            // Register services
            ServiceContainer services = new ServiceContainer();
            services.AddService<IGraphicsDeviceService>(graphics);
            mServices = services;

            // Store the window handle
            mWindowHandle = windowHandle;

            // Get the client bounds before Initializing any FlatRedBall objects
            // so that objects can use these properties immediately.
            mClientWidth = mGraphicsOptions.ResolutionWidth;
            mClientHeight = mGraphicsOptions.ResolutionHeight;

            graphics.DeviceReset += new EventHandler<EventArgs>(graphics_DeviceReset);

#if !MONOGAME
            System.Windows.Forms.Form.FromHandle(mWindowHandle).Resize += new EventHandler(Window_ClientSizeChanged);

#endif
            CommonInitialize(graphics);
        }

        private static void CommonInitialize(IGraphicsDeviceService graphicsService)
        {
            FinishInitialization(graphicsService);

        }


            #region XML Docs
        /// <summary>
        /// Basic initialization steps (commond to all initialization methods)
        /// </summary>
            #endregion
        private static void FinishInitialization(IGraphicsDeviceService graphics)
        {
            // All InitializeFlatRedBall methods call this one

#if !MONOGAME
            PngLoader.Initialize();
#endif

            Texture2D fontTexture = null;

            #region Set up the resources manager and load fonts

#if XNA4
            
            fontTexture = FlatRedBall.Content.ContentManager.GetDefaultFontTexture(graphics.GraphicsDevice);
            
            fontTexture.Name = "Default Font Texture";

            var fontPattern = DefaultFontDataColors.GetFontPattern();
            TextManager.DefaultFont = new BitmapFont(fontTexture, fontPattern);

#elif WINDOWS_8
            Assembly assembly = typeof(Sprite).GetTypeInfo().Assembly;
            
            using (Stream stream = assembly.GetManifestResourceStream("FlatRedBallW8.Assets.defaultText.png"))
            {
                fontTexture = Texture2D.FromStream(graphics.GraphicsDevice, stream);
                fontTexture.Name = "Default Font Texture";
            }

            string fontPattern = null;

            using (Stream stream = assembly.GetManifestResourceStream("FlatRedBallW8.Assets.defaultFont.txt"))
            using (System.IO.StreamReader sr = new StreamReader(stream))
            {
                fontPattern = sr.ReadToEnd();
            }

            TextManager.DefaultFont = new BitmapFont(fontTexture, fontPattern);


#elif !MONOGAME
            mResourceContentManager = new Microsoft.Xna.Framework.Content.ResourceContentManager(
                mServices, FlatRedBall.Resources.x86.Resources.ResourceManager);

            fontTexture = mResourceContentManager.Load<Texture2D>("defaultText");
            fontTexture.Name = "Default Font Texture";
            TextManager.DefaultFont = new BitmapFont(fontTexture,
                FlatRedBall.Resources.x86.Resources.defaultFont);
#endif

            #endregion

            #region Initialize the Managers
            InstructionManager.Initialize();
            TimeManager.Initialize();
            ShapeManager.Initialize();

            Renderer.Initialize(graphics);
            SpriteManager.Initialize();
            TextManager.Initialize(graphics.GraphicsDevice);

            InputManager.Initialize(mWindowHandle);

            InputManager.Update();


            // We're getting rid of this
            //BroadcastManager.Initialize();

            #endregion

            InitializeShaders();

            mIsInitialized = true;




            GuiManager.Initialize(fontTexture, new Cursor(SpriteManager.Camera));


            
        }

        public const string ShaderContentManager = "InternalShaderContentManager";
        public static void InitializeShaders()
        {

#if WINDOWS
            if(mResourceContentManager != null)
            {
                mResourceContentManager.Dispose();
            }

            mResourceContentManager = new Microsoft.Xna.Framework.Content.ResourceContentManager(
                mServices, FlatRedBall.Resources_Xna_4.x86.Resources.ResourceManager);



            Renderer.Effect = mResourceContentManager.Load<Effect>("FlatRedBallShader");
#endif
        }

            #region Graphics Device Reset Events

        static void graphics_PreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs e)
        {
            PresentationParameters presentationParameters = e.GraphicsDeviceInformation.PresentationParameters;
            mGraphicsOptions.SetPresentationParameters(ref presentationParameters);
            e.GraphicsDeviceInformation.PresentationParameters = presentationParameters;
        }

        static bool mHandlingReset = false;


        internal static void graphics_DeviceReset(object sender, EventArgs e)
        {

            if (mGraphicsOptions == null) return;

            HandleResize();

            // The following line causes a crash in the current effect in Renderer because it isn't set when you first run the app.
            //Renderer.ForceSetColorOperation(Renderer.ColorOperation);
            Renderer.ForceSetBlendOperation();

        }

        private static void HandleResize()
        {
            bool hasResolutionChanged = mClientWidth != mGraphicsOptions.ResolutionWidth ||
                mClientHeight != mGraphicsOptions.ResolutionHeight;

            if (hasResolutionChanged && !mHandlingReset && mGraphicsOptions.ResolutionHeight != 0 && mGraphicsOptions.ResolutionWidth != 0)
            {
                mHandlingReset = true;

                // Update client sizes
                mClientWidth = mGraphicsOptions.ResolutionWidth;
                mClientHeight = mGraphicsOptions.ResolutionHeight;

                //Window_ClientSizeChanged(sender, e);

                // Update cameras
                foreach (Camera camera in SpriteManager.Cameras)
                {
                    camera.UpdateOnResize();
                }

                mHandlingReset = false;
            }
        }

            #endregion

            #endregion

        #region Public Methods

#if MONODROID
        internal static string Normalize(string FileName)
        {
            FileName = FileName.Replace('\\', Path.DirectorySeparatorChar);
            int index = FileName.LastIndexOf(Path.DirectorySeparatorChar);
            string path = string.Empty;
            string file = FileName;
            if (index >= 0)
            {
                file = FileName.Substring(index + 1, FileName.Length - index - 1);
                path = FileName.Substring(0, index);
            }
            string[] files = Game.Activity.Assets.List(path);

            if (Contains(file, files))
                return FileName;

            // Check the file extension
            if (!string.IsNullOrEmpty(Path.GetExtension(FileName)))
            {
                return null;
            }

            return Path.Combine(path, TryFindAnyCased(file, files, ".xnb", ".jpg", ".bmp", ".jpeg", ".png", ".gif"));
        }

        private static bool Contains(string search, string[] arr)
        {
            return arr.Any(s => s == search);
        }

        private static string TryFindAnyCased(string search, string[] arr, params string[] extensions)
        {
            return arr.FirstOrDefault(s => extensions.Any(ext => s.ToLower() == (search.ToLower() + ext)));
        }

#if ASK_VIC //MDS_TODO ASK VIC
        public static void PreInitializeDraw(Game game, string textureToDraw)
        {
            var texture = Texture2D.FromFile(game.GraphicsDevice, Game.Activity.Assets.Open(Normalize(textureToDraw)));
            var spriteBatch = new SpriteBatch(game.GraphicsDevice);

            texture.Apply();

            game.GraphicsDevice.Clear(Color.Black);

            spriteBatch.Begin();

            spriteBatch.Draw(texture,
                            new Rectangle(0, 0, game.GraphicsDevice.Viewport.Width, game.GraphicsDevice.Viewport.Height),
                                          Color.White);

            spriteBatch.End();

            game.GraphicsDevice.Present();

            spriteBatch.Dispose();
            game.Content.Unload();
        }

        public static void PreInitializeDraw(Game game, string textureToDraw, Rectangle sourceRect)
        {
            var texture = Texture2D.FromFile(game.GraphicsDevice, Game.Activity.Assets.Open(Normalize(textureToDraw)));
            var spriteBatch = new SpriteBatch(game.GraphicsDevice);

            texture.Apply();

            game.GraphicsDevice.Clear(Color.Black);

            spriteBatch.Begin();

            spriteBatch.Draw(   texture, 
                                new Rectangle(0, 0, game.GraphicsDevice.Viewport.Width, game.GraphicsDevice.Viewport.Height),
                                sourceRect,
                                Color.White);

            spriteBatch.End();

            game.GraphicsDevice.Present();

            texture.Dispose();
            spriteBatch.Dispose();
        }
#endif // IF 0

#endif

        public static void AddManager(IManager managerToAdd)
        {
            mManagers.Add(managerToAdd);
        }

        public static void RemoveManager(IManager managerToRemove)
        {
            mManagers.Remove(managerToRemove);

        }

        public static void AddDisposable(string disposableName, IDisposable disposable, string contentManagerName)
        {
            GetContentManagerByName(contentManagerName).AddDisposable(disposableName, disposable);
        }

        public static void AddNonDisposable(string objectName, object objectToAdd, string contentManagerName)
        {
            GetContentManagerByName(contentManagerName).AddNonDisposable(objectName, objectToAdd);
        }

        public static bool CanLoadType(Type type)
        {
            return mTypesThatCanBeLoaded.Contains(type);
        }

        public static bool HasContentManager(string contentManagerName)
        {
            return mContentManagers.ContainsKey(contentManagerName);
        }

        public static FlatRedBall.Content.ContentManager GetContentManagerByName(string contentManagerName)
        {
            lock (mContentManagers)
            {
                if (string.IsNullOrEmpty(contentManagerName))
                {
                    if (!mContentManagers.ContainsKey(GlobalContentManager))
                    {
                        return CreateContentManagerByName(GlobalContentManager);
                    }
                    else
                    {
                        return mContentManagers[GlobalContentManager];
                    }
                }
                else if (mContentManagers.ContainsKey(contentManagerName))
                {
                    return mContentManagers[contentManagerName];
                }
                else
                {
                    return CreateContentManagerByName(contentManagerName);
                }
            }
        }

        public static T GetNonDisposable<T>(string objectName, string contentManagerName)
        {
            return GetContentManagerByName(contentManagerName).GetNonDisposable<T>(objectName);
        }

        public static bool IsThreadPrimary()
        {
#if WINDOWS_8 || UWP
            int threadId = Environment.CurrentManagedThreadId;
#else
            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
            return threadId == mPrimaryThreadId;

        }

        #region Load/IsLoaded/Unload asset methods

#if !FRB_MDX && !MONOGAME
        internal static Model Load(string modelName, string contentManagerName)
        {
#if DEBUG
            if (!mIsInitialized)
            {
                throw new InvalidOperationException("FlatRedBall is not initialized yet");
            }
#endif
            if (FileManager.GetExtension(modelName) != "")
            {
                return Load<Model>(modelName, contentManagerName);
                //                throw new System.ArgumentException("Cannot load models from " +
                //                  FileManager.GetExtension(modelName) + " file.  Add the model to the project and " +
                //                "load without the extension");
            }
            else
            {
                FlatRedBall.Content.ContentManager contentManager = GetContentManagerByName(contentManagerName);
                //#if PROFILE
                //            bool exists = false;

                //            exists = ((FlatRedBall.Content.ContentManager)contentManager).IsAssetLoaded(modelName);

                //            if (exists)
                //            {
                //                mHistory.Add(new ContentLoadHistory(
                //                    TimeManager.CurrentTime, typeof(Model).Name, modelName, ContentLoadDetail.Cached));
                //            }
                //            else
                //            {
                //                mHistory.Add(new ContentLoadHistory(
                //                    TimeManager.CurrentTime, typeof(T).Name, assetName, ContentLoadDetail.HddFromContentPipeline));
                //            }
                //#endif

                return (Model)contentManager.LoadFromProject<Model>(modelName);
            }
        }

        internal static bool ModelHasAnimation(string modelName)
        {
            bool found = false;
            string line;

            // Create the regular expression and open the file
            Regex regx = new Regex("AnimationSet .* {");

            StreamReader sr = null;

            if (FileManager.IsRelative(modelName))
            {
                sr = new StreamReader(FileManager.RelativeDirectory + modelName);
            }
            else
            {
                sr = new StreamReader(modelName);
            }


            // Try to find
            while (!found && (line = sr.ReadLine()) != null)
            {
                if (regx.IsMatch(line))
                    found = true;
            }

            // Close the file
            sr.Close();

            // return the success value
            return found;
        }
#endif

        public static bool IsLoaded<T>(string assetName, string contentManagerName)
        {
            return GetContentManagerByName(contentManagerName).IsAssetLoadedByName<T>(assetName);
        }

        public static T Load<T>(string assetName)
        {
            return Load<T>(assetName, GlobalContentManager);
        }

        public static T Load<T>(string assetName, string contentManagerName)
        {
#if DEBUG
            if (!mIsInitialized)
            {
                throw new InvalidOperationException("FlatRedBall is not initialized yet");
            }
#endif
            FlatRedBall.Content.ContentManager contentManager = GetContentManagerByName(contentManagerName);

            if (FlatRedBall.Content.ContentManager.LoadFromGlobalIfExists
#if DEBUG
 || FlatRedBall.Content.ContentManager.ThrowExceptionOnGlobalContentLoadedInNonGlobal
#endif
)
            {
                if (contentManagerName != GlobalContentManager &&
                    IsLoaded<T>(assetName, GlobalContentManager))
                {

                    if (FlatRedBall.Content.ContentManager.LoadFromGlobalIfExists)
                    {
                        return GetContentManagerByName(GlobalContentManager).Load<T>(assetName);
                    }
#if DEBUG
                    if (FlatRedBall.Content.ContentManager.ThrowExceptionOnGlobalContentLoadedInNonGlobal)
                    {
                        throw new Exception("The file " + assetName + " is already loaded in the Global ContentManager.  " +
                            "The game is attempting to load it in the following ContentManager: " + contentManagerName);
                    }
#endif
                }

            }

            T toReturn = contentManager.Load<T>(assetName);
            return toReturn;
        }

        public static void Unload<T>(T assetToUnload, string contentManagerName)
        {
            FlatRedBall.Content.ContentManager contentManager = GetContentManagerByName(contentManagerName);
            contentManager.UnloadAsset<T>(assetToUnload);
        }

        public static void Unload(string contentManagerName)
        {
            if (contentManagerName == FlatRedBallServices.GlobalContentManager)
            {
                throw new ArgumentException("Cannot unload the Global content manager.  " +
                    "FlatRedBallServices.GlobalContentManager is a content manager that should never be unloaded.");
            }



            if (mContentManagers.ContainsKey(contentManagerName))
            {
                FlatRedBall.Content.ContentManager contentManager = mContentManagers[contentManagerName];
                contentManager.Unload();
                mContentManagers.Remove(contentManagerName);
            }
        }

        public static void Clean()
        {
            //Go through and clean up any referances that may be holding on to resources
            GuiManager.mLastWindowWithFocus = null;

#if !FRB_MDX
            SpriteManager.ClearParticleTextures();
#endif

#if XNA4
            lock (mContentManagers)
            {
                Content.ContentLoaders.TextureContentLoader.ClearPremultipliedAlphaImageData();
            }
#endif
        }

#if !FRB_MDX
        public static void OnDeviceLost()
        {
            // Vic says - I don't think we need this anymore.

            //foreach (KeyValuePair<string, ContentManager> kvp in mContentManagers)
            //{
            //    ContentManager contentManager = kvp.Value;
            //    // fill in code here!!!
            //}

            Dictionary<Sprite, string> textureNames = new Dictionary<Sprite, string>();

            foreach (Sprite s in SpriteManager.AutomaticallyUpdatedSprites)
            {
                textureNames.Add(s, s.Texture.Name);
            }

            foreach (Sprite s in SpriteManager.ManuallyUpdatedSprites)
            {
                textureNames.Add(s, s.Texture.Name);
            }

            // Justin Johnson 04/2015: Retired particle blueprint system
            foreach (FlatRedBall.Graphics.Particle.Emitter emitter in SpriteManager.Emitters)
            {
                // TODO: not sure how to add texture names since 
                // no sprite exists without the particle blueprint
                // if we still need this, the dictionary needs to support
                // texture names with no sprite key
                // textureNames.Add(emitter.ParticleBlueprint, emitter.ParticleBlueprint.Texture.Name);
            }

            List<string> contentManagers = new List<string>();
            List<string> fileNames = new List<string>();

            // Vic says: I don't think we need this anymore:

            //foreach (FlatRedBall.Content.ContentManager contentManager in mContentManagers.Values)
            //{
            //    contentManager.RefreshTextureOnDeviceLost();
            //}


            for (int i = 0; i < fileNames.Count; i++)
            {
                switch (FileManager.GetExtension(fileNames[i]))
                {
                    case "bmp":
                    case "dds":
                    case "dib":
                    case "hdr":
                    case "jpg":
                    case "pfm":
                    case "png":
                    case "ppm":
                    case "tga":
                        Load<Texture2D>(fileNames[i], contentManagers[i]);
                        break;

                }
            }

            foreach (KeyValuePair<Sprite, string> kvp in textureNames)
            {
                kvp.Key.Texture = Load<Texture2D>(kvp.Value);
            }

            TextManager.RefreshBitmapFontTextures();
        }
#endif

        #endregion

        public static void ForceClientSizeUpdates()
        {
            Window_ClientSizeChanged(null, null);
        }

        public static void ReplaceTexture(Texture2D oldTexture, Texture2D newTexture)
        {
            // Replace the texture for all objects managed by all of the managers
            SpriteManager.ReplaceTexture(oldTexture, newTexture);
            TextManager.ReplaceTexture(oldTexture, newTexture);
        }

        #region XML Docs
        /// <summary>
        /// Attempts to replace the texture. Will only work if the texture is loaded from file.
        /// </summary>
        /// <param name="oldTexture">Reference to the old texture</param>
        /// <param name="newTexture">Reference to the new texture</param>
        /// <param name="contentManagerName">The name of the content manager containing the texture</param>
        #endregion
        public static void ReplaceFromFileTexture2D(Texture2D oldTexture, Texture2D newTexture, string contentManagerName)
        {
            FlatRedBall.Content.ContentManager contentManager = GetContentManagerByName(contentManagerName);

            if (contentManager == null)
            {
                throw new ArgumentException("There is no content manager by the name " + contentManagerName);

            }

            ReplaceTexture(oldTexture, newTexture);

            if (contentManager.IsAssetLoadedByReference(oldTexture))
            {
                contentManager.UnloadAsset<Texture2D>(oldTexture);
            }
        }

        static int mNumberOfThreads = 1;

        public static int GetNumberOfThreadsToUse()
        {
            return mNumberOfThreads;
        }

        public static void SetNumberOfThreadsToUse(int count)
        {
#if DEBUG
            if (count <= 0)
            {
                throw new ArgumentException("Negative values and values of 0 are not allowed", "count");
            }
#endif

#if FRB_MDX
            if (count != 1)
            {
                throw new ArgumentException("FlatRedBall MDX only supports single-threaded processing.");
            }
#else
            mNumberOfThreads = count;
            SpriteManager.SetNumberOfThreadsToUse(count);
            Renderer.SetNumberOfThreadsToUse(count);
#endif
        }

        #region Every-frame Methods (Update and Draw)


        public static void Update(GameTime gameTime)
        {
            Update(gameTime, null);
        }


        public static void Update(GameTime gameTime, Section section)
        {
            if (!mIsSuspended)
            {
                if (section != null)
                {
                    Section.GetAndStartContextAndTime("Start of update");
                }
                Debugging.Debugger.Update();

#if FRB_MDX

            if (!mIsInitialized)
            {
                return;
            }

            TimeManager.Update();
#else
                TimeManager.Update(gameTime);
#endif

                InputManager.Update();
                // The InstructionManager should be updated BEFORE
                // any other managers.  The reason is because Instructions
                // are dependent on time, and these instructions could set properties
                // which are needed in the individual objects' update methods.
                // InstructionManager Update should happen *after* InputManager.Update 
                // in case any instructions want to override input code.
                InstructionManager.Update(TimeManager.CurrentTime);

                if (section != null)
                {
                    Section.EndContextAndTime();
                    Section.GetAndStartContextAndTime("ShapeManager Update");
                }
                ShapeManager.Update();

                if (section != null)
                {
                    Section.EndContextAndTime();
                    section = Section.GetAndStartContextAndTime("SpriteManager Update");
                }
                SpriteManager.Update(section);

                if (section != null)
                {
                    Section.EndContextAndTime();
                    Section.GetAndStartContextAndTime("TextManager Update");
                }
                TextManager.Update();


                if (section != null)
                {
                    Section.EndContextAndTime();
                    Section.GetAndStartContextAndTime("End of Update");
                }

#if !FRB_MDX
                AudioManager.Update();

                Renderer.Update();

#endif

#if !FRB_MDX && !MONOGAME && !XNA4
                if (Renderer.UseRenderTargets)
                {
                    PostProcessingManager.Update();
                }
#endif

                foreach (IManager manager in mManagers)
                {
                    manager.Update();
                }

                DestroyContentManagersReadyToBeDestroyed();


                // We used to do this earlier in the frame but I think it should
                // be later because it can cause custom code to be executed.  So lets
                // move it here:
#if !XBOX360
                if (!mIsCommandLine)
                {
                    GuiManager.Control();
                }
#if SUPPORTS_FRB_DRAWN_GUI
                GuiManager.Animate();
#endif


#endif


                //TimeManager.TimeSection("InstructionManager Update");

                // Getting rid of this
                //BroadcastManager.Update();
                if (section != null)
                {
                    Section.EndContextAndTime();
                }
            }
        }

#if XNA4 && !XBOX360 && !WINDOWS_PHONE && !MONOGAME
        #region XML Docs
        /// <summary>
        /// The update method for command line games.  Does not render anything to the screen
        /// </summary>
        /// <param name="gameTime">The GameTime</param>
        #endregion
        public static void UpdateCommandLine(GameTime gameTime)
        {
            Debugging.Debugger.Update();

            TimeManager.Update(gameTime);

            InstructionManager.Update(TimeManager.CurrentTime);

            ShapeManager.Update();

            SpriteManager.Update();

            DestroyContentManagersReadyToBeDestroyed();

        }
#endif

        public static void Draw()
        {
            Draw(null);
        }

        public static void Draw(Section section)
        {
#if DEBUG
            if (mIsCommandLine)
            {
                throw new InvalidOperationException("Drawing not supported in command line mode");
            }
#endif

            if (!mIsSuspended)
            {
                DestroyLoadingScreenSpriteBatches();

#if PROFILE
                TimeManager.TimeSection("Start of Draw");
#endif
                if (section != null)
                {
                    Section.GetAndStartContextAndTime("Renderer Update");
                }
                UpdateDependencies();

                if (section != null)
                {
                    Section.EndContextAndTime();
                    Section.GetAndStartContextAndTime("Rendering everything");
                }
                RenderAll(section);

                if (section != null)
                {
                    Section.EndContextAndTime();
                }
#if PROFILE
            PrintProfilingInformation();
#endif
            }
#if !FRB_MDX
            else
            {
                PerformSuspendedDraw();
            }
#endif

        }

#if !FRB_MDX
        private static void PerformSuspendedDraw()
        {
            GraphicsDevice.Clear(Color.Black);

            if (_textureToDraw != null)
            {
                if (_loadingScreenSpriteBatch == null)
                {
                    _loadingScreenSpriteBatch = new SpriteBatch(Game.GraphicsDevice);
                }

                // MDS_TODO ASK_VIC
                //#if MONODROID
                //                    _textureToDraw.Apply();
                //#endif

                _loadingScreenSpriteBatch.Begin();

                _loadingScreenSpriteBatch.Draw(_textureToDraw,
                                       new Rectangle(0, 0, Game.GraphicsDevice.Viewport.Width, Game.GraphicsDevice.Viewport.Height),
                                       _sourceRect,
                                       Color.White);

                //_loadingScreenSpriteBatch.Draw(_textureToDraw, 
                //                                 Vector2.Zero, 
                //                                 new Rectangle(16, 96, 480, 320),
                //                                               Color.White);

                _loadingScreenSpriteBatch.End();
            }
        }
#endif

        public static void RenderAll(Section section)
        {
            lock (Renderer.Graphics.GraphicsDevice)
            {
                Renderer.Draw(section);
            }
        #if PROFILE
            TimeManager.TimeSection("Renderer.Draw();");
        #endif


            // Just in case code is going to modify any of the textures that were used in rendering:
            Renderer.Texture = null;
            GraphicsDevice.Textures[0] = null;
        }

        /// <summary>
        /// Calls UpdateDependencies on all contained managers.
        /// </summary>
        public static void UpdateDependencies()
        {


            // Update all PositionedObject dependencies so that objects
            // always appear to have the same relative position from their parents
            SpriteManager.UpdateDependencies();

#if PROFILE
            TimeManager.TimeSection("SpriteManager.UpdateDependencies();");
#endif

            // We should update the Debugger the SpriteManager, because
            // the SpriteManager handles the Camera updating.
            // The debugger adjusts its positions and sizes according to the
            // Camera.
            FlatRedBall.Debugging.Debugger.UpdateDependencies();
            TextManager.UpdateDependencies();

#if PROFILE
            TimeManager.TimeSection("TextManager.UpdateDependencies()");
#endif

            ShapeManager.UpdateDependencies();

#if PROFILE
            TimeManager.TimeSection("ShapeManager.UpdateDependencies();");
#endif

#if !FRB_MDX

            AudioManager.UpdateDependencies();

#if PROFILE
            TimeManager.TimeSection("AudioManager.UpdateDependencies();");
#endif

            Renderer.UpdateDependencies();

#if PROFILE
            TimeManager.TimeSection("Renderer.UpdateDependencies()");
#endif

            foreach (IManager manager in mManagers)
            {
                manager.UpdateDependencies();
            }
#endif


        }

        private static void DestroyLoadingScreenSpriteBatches()
        {
#if !FRB_MDX
            if (_textureToDraw != null)
            {
                _textureToDraw.Dispose();
                _textureToDraw = null;
            }

            if (_loadingScreenSpriteBatch != null)
            {
                _loadingScreenSpriteBatch.Dispose();
                _loadingScreenSpriteBatch = null;
            }
#endif
        }
        #endregion


        #region Suspending

        public static void SuspendEngine()
        {
            lock (mSuspendLockObject)
            {
                if (!mIsSuspended)
                {
                    mIsSuspended = true;

                    if (Suspending != null)
                    {
                        Suspending(null, null);
                    }
                }
            }
        }

#if !FRB_MDX && !XNA3
        public static void SuspendEngine(string textureToDraw, Rectangle sourceRect)
        {
            Unload("SuspendEngine");
#if MONOGAME
            throw new NotImplementedException();
#else
            using (Stream stream = File.OpenRead(textureToDraw))
            {
                _textureToDraw = Texture2D.FromStream(GraphicsDevice, stream);
            }

            _sourceRect = sourceRect;
            SuspendEngine();
#endif


        }

        public static void UnsuspendEngine()
        {
            lock (mSuspendLockObject)
            {
                if (mIsSuspended)
                {
                    if (Unsuspending != null)
                    {
                        Unsuspending(null, null);
                    }

                    mIsSuspended = false;
                }
            }
        }
#endif

        #endregion

#if !XBOX360 && !WINDOWS_PHONE && !MONOGAME
        public static Texture2D BitmapToTexture2D(System.Drawing.Bitmap bitmapToConvert,
            string newTextureName, string contentManagerName)
        {
            Texture2D newTexture = null;

#if FRB_XNA

#if !XNA4
            using (MemoryStream s = new MemoryStream())
            {
                bitmapToConvert.Save(s, System.Drawing.Imaging.ImageFormat.Png);
                s.Seek(0, SeekOrigin.Begin); //must do this, or error is thrown in next line
                newTexture = Texture2D.FromFile(mGraphics.GraphicsDevice, s);
            }
#endif


            return newTexture;
#else
            newTexture = new Texture2D();
            newTexture.texture = Microsoft.DirectX.Direct3D.Texture.FromBitmap(
                GraphicsDevice, bitmapToConvert, Usage.AutoGenerateMipMap, Microsoft.DirectX.Direct3D.Pool.Managed);

            newTexture.Name = newTextureName;

            newTexture.Width = bitmapToConvert.Width;
            newTexture.Height = bitmapToConvert.Height;

            return newTexture;

                
#endif

            //Color[] pixels = new Color[bitmapToConvert.Width * bitmapToConvert.Height];
            //for (int y = 0; y < bitmapToConvert.Height; y++)
            //{
            //    for (int x = 0; x < bitmapToConvert.Width; x++)
            //    {
            //        System.Drawing.Color c = bitmapToConvert.GetPixel(x, y);
            //        pixels[(y * bitmapToConvert.Width) + x] = new Color(c.R, c.G, c.B, c.A);
            //    }
            //}

            //Texture2D newTexture = new Texture2D(
            //  mGraphics.GraphicsDevice,
            //  bitmapToConvert.Width,
            //  bitmapToConvert.Height,
            //  1,
            //  TextureUsage.None,
            //  SurfaceFormat.Color);

            //newTexture.SetData<Color>(pixels);

            //AddDisposable(newTextureName, newTexture, contentManagerName);

            //return newTexture;
        }
#endif



#if FRB_XNA

        #region Debugging and Profiling

        public static string GetRenderingPerformanceInformation()
        {
            //          StringBuilder stringBuilder = new StringBuilder()
            return Renderer.ToString();
        }

        public static string GetGeometryPerformanceInformation()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# Polygon.CollideAgainst calls: ").AppendLine(Polygon.NumberOfTimesCollideAgainstPolygonCalled.ToString());
            sb.Append("# Radius Test Passes: ").Append(Polygon.NumberOfTimesRadiusTestPassed.ToString());
            return sb.ToString();
        }

        public static string GetContentManagerInformation()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Content from content pipeline:");
            //foreach (KeyValuePair<string, FlatRedBall.Content.ContentManager>kvp in mContentManagers)
            //{
            //    stringBuilder.Append(" Content Manager: ").AppendLine(kvp.Key);

            //    foreach (KeyValuePair<string, object> internalKvp in kvp.Value.mAssets)
            //    {
            //        stringBuilder.AppendLine("  " + internalKvp.Key.ToString());
            //    }
            //}


            //stringBuilder.AppendLine();
            //stringBuilder.AppendLine("Content from file:");
            //foreach (KeyValuePair<string, Dictionary<string, System.IDisposable>> dictionaryKeyValuePair in mDisposableDictionaries)
            //{
            //    stringBuilder.Append(" Content Manager: ").AppendLine(dictionaryKeyValuePair.Key);
            //    foreach (KeyValuePair<string, IDisposable> kvp in dictionaryKeyValuePair.Value)
            //    {
            //        stringBuilder.AppendLine("  " + kvp.Key);

            //    }
            //}

            // TODO:  Add the NonDisposable info here

            return stringBuilder.ToString();
        }

        public static string GetManagerInformation()
        {
            StringBuilder stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("There are " + SpriteManager.Cameras.Count + " Cameras");
            stringBuilder.AppendLine("There are " + SpriteManager.AutomaticallyUpdatedSprites.Count + " AutomaticallyUpdatedSprites");
            stringBuilder.AppendLine("There are " + SpriteManager.ManuallyUpdatedSpriteCount + " ManuallyUpdatedSprites");
            stringBuilder.AppendLine("There are " + SpriteManager.ManagedPositionedObjects.Count + " ManagedPositionedObjects");
            stringBuilder.AppendLine("There are " + SpriteManager.ParticleCount + " Particle Sprites");
            stringBuilder.AppendLine("There are " + SpriteManager.Emitters.Count + " Emitters");
            stringBuilder.AppendLine("There are " + SpriteManager.LayersWriteable.Count + " Layers");
            stringBuilder.AppendLine("There are " + SpriteManager.SpriteFrames.Count + " SpriteFrames");
            stringBuilder.AppendLine("There are " + SpriteManager.DrawableBatches.Count + " DrawableBatches");

            stringBuilder.AppendLine("There are " + TextManager.AutomaticallyUpdatedTexts.Count + " AutomaticallyUpdatedTexts");

            stringBuilder.AppendLine("There are " + ShapeManager.AutomaticallyUpdatedShapes.Count + " AutomaticallyUpdatedShapes");
            stringBuilder.AppendLine("There are " + ShapeManager.VisibleCircles.Count + " Visibile Circles");
            stringBuilder.AppendLine("There are " + ShapeManager.VisibleRectangles.Count + " Visibile AxisAlignedRectangles");
            stringBuilder.AppendLine("There are " + ShapeManager.VisiblePolygons.Count + " Visibile Polygons");
            stringBuilder.AppendLine("There are " + ShapeManager.VisibleLines.Count + " Visibile Lines");

            return stringBuilder.ToString();

        }

        #endregion
#endif

#if PROFILE


        static double mLastProfilePrint;
        private static void PrintProfilingInformation()
        {
            const double profilePrintFrequency = 4f; // how many seconds

            if (TimeManager.SecondsSince(mLastProfilePrint) > profilePrintFrequency)
            {
                System.Console.WriteLine("== Profile Time: " + TimeManager.CurrentTime + " ==");

                System.Console.WriteLine();

                System.Console.WriteLine(TimeManager.GetTimedSections(true));
                System.Console.WriteLine();

                System.Console.WriteLine("== Sum Profile Time: " + TimeManager.CurrentTime + " ==");

                System.Console.WriteLine();

                System.Console.WriteLine(TimeManager.GetSumTimedSections());
                System.Console.WriteLine();
                
                mLastProfilePrint = TimeManager.CurrentTime;

                
            }
        }
#endif

        #endregion

        #region Internal Methods



        internal static Dictionary<string, IDisposable> GetDisposableDictionary(string contentManagerName)
        {
            return GetContentManagerByName(contentManagerName).mDisposableDictionary;
        }

        internal static void MoveContentManagerToWaitingToUnloadList(FlatRedBall.Content.ContentManager contentManager)
        {
            mContentManagersWaitingToBeDestroyed.Add(contentManager);

            // The ContentManager should exist in both the 
            // mContentManagersWaitingToBeDestroyed list as
            // well as mContentManagers because the separate
            // thread that hasn't died yet may still be adding
            // content.  If that's the case, we don't want it to
            // make a new ContentManager.
            //mContentManagers.Remove(contentManager.Name);
        }

        #endregion

        #region Private Methods

        private static FlatRedBall.Content.ContentManager CreateContentManagerByName(string contentManagerName)
        {
            // mServices will be null if this is run from unit tests or command line so let's use 
            FlatRedBall.Content.ContentManager contentManager = new FlatRedBall.Content.ContentManager(
                contentManagerName,
                mServices);

            if (mIsCommandLine)
            {
                mContentManagers.Add(contentManagerName, contentManager);
            }
            else
            {
                // We gotta lock the GraphicsDevice so that the dictionary isn't being 
                // modified while rendering occurs.
                lock (Renderer.Graphics.GraphicsDevice)
                {
                    mContentManagers.Add(contentManagerName, contentManager);
                }
            }

            return contentManager;
        }

        public static void DestroyContentManagersReadyToBeDestroyed()
        {
            for (int i = mContentManagersWaitingToBeDestroyed.Count - 1; i > -1; i--)
            {
                FlatRedBall.Content.ContentManager contentManager = mContentManagersWaitingToBeDestroyed[i];

                if (!contentManager.IsWaitingOnAsyncLoadsToFinish)
                {
                    contentManager.Unload();
                    mContentManagersWaitingToBeDestroyed.RemoveAt(i);
                    mContentManagers.Remove(contentManager.Name);
                }
            }
        }

        #endregion

        #endregion
    }
}
