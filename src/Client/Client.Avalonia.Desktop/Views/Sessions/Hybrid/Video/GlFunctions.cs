using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;

namespace Client.Views.Sessions.Hybrid.Video;

/// <summary>
/// The OpenGL and EGL calls the video presenter makes, loaded from the context of the control as
/// function pointers.
/// </summary>
internal sealed unsafe class GlFunctions
{
    public const int Texture2D = 0x0DE1;
    public const int Texture0 = 0x84C0;
    public const int TextureMinFilter = 0x2801;
    public const int TextureMagFilter = 0x2800;
    public const int TextureWrapS = 0x2802;
    public const int TextureWrapT = 0x2803;
    public const int Linear = 0x2601;
    public const int ClampToEdge = 0x812F;
    public const int UnpackAlignment = 0x0CF5;
    public const int UnpackRowLength = 0x0CF2;
    public const int Rgba = 0x1908;
    public const int Rgba8 = 0x8058;
    public const int R8 = 0x8229;
    public const int Rg8 = 0x822B;
    public const int Red = 0x1903;
    public const int Rg = 0x8227;
    public const int UnsignedByte = 0x1401;
    public const int Framebuffer = 0x8D40;
    public const int ColorBufferBit = 0x4000;
    public const int TriangleStrip = 0x0005;
    public const int VertexShader = 0x8B31;
    public const int FragmentShader = 0x8B30;
    public const int CompileStatus = 0x8B81;
    public const int LinkStatus = 0x8B82;
    public const int ColorAttachment0 = 0x8CE0;
    public const int FramebufferComplete = 0x8CD5;
    public const int ScissorTest = 0x0C11;
    public const int Blend = 0x0BE2;
    public const int DepthTest = 0x0B71;
    public const int StencilTest = 0x0B90;
    public const int CullFace = 0x0B44;
    public const int PixelUnpackBuffer = 0x88EC;

    public delegate* unmanaged<int, uint*, void> GenTextures;
    public delegate* unmanaged<int, uint*, void> DeleteTextures;
    public delegate* unmanaged<int, uint, void> BindTexture;
    public delegate* unmanaged<int, void> ActiveTexture;
    public delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void> TexImage2D;
    public delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void> TexSubImage2D;
    public delegate* unmanaged<int, int, int, void> TexParameteri;
    public delegate* unmanaged<int, int, void> PixelStorei;
    public delegate* unmanaged<int, uint> CreateShader;
    public delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource;
    public delegate* unmanaged<uint, void> CompileShader;
    public delegate* unmanaged<uint, int, int*, void> GetShaderiv;
    public delegate* unmanaged<uint, int, int*, byte*, void> GetShaderInfoLog;
    public delegate* unmanaged<uint, void> DeleteShader;
    public delegate* unmanaged<uint> CreateProgram;
    public delegate* unmanaged<uint, uint, void> AttachShader;
    public delegate* unmanaged<uint, void> LinkProgram;
    public delegate* unmanaged<uint, int, int*, void> GetProgramiv;
    public delegate* unmanaged<uint, void> DeleteProgram;
    public delegate* unmanaged<uint, void> UseProgram;
    public delegate* unmanaged<uint, byte*, int> GetUniformLocation;
    public delegate* unmanaged<int, int, void> Uniform1i;
    public delegate* unmanaged<int, float, float, float, void> Uniform3f;
    public delegate* unmanaged<int, int, byte, float*, void> UniformMatrix3fv;
    public delegate* unmanaged<int, uint*, void> GenVertexArrays;
    public delegate* unmanaged<int, uint*, void> DeleteVertexArrays;
    public delegate* unmanaged<uint, void> BindVertexArray;
    public delegate* unmanaged<int, int, int, void> DrawArrays;
    public delegate* unmanaged<int, int, int, int, void> Viewport;
    public delegate* unmanaged<float, float, float, float, void> ClearColor;
    public delegate* unmanaged<int, void> Clear;
    public delegate* unmanaged<int, uint, void> BindFramebuffer;
    public delegate* unmanaged<int, uint*, void> GenFramebuffers;
    public delegate* unmanaged<int, uint*, void> DeleteFramebuffers;
    public delegate* unmanaged<int, int, int, uint, int, void> FramebufferTexture2D;
    public delegate* unmanaged<int, int> CheckFramebufferStatus;
    public delegate* unmanaged<int, void> Disable;
    public delegate* unmanaged<byte, byte, byte, byte, void> ColorMask;
    public delegate* unmanaged<int, uint, void> BindBuffer;

    /// <summary>Null below OpenGL 3.3 and OpenGL ES 3.0, where there are no sampler objects.</summary>
    public delegate* unmanaged<uint, uint, void> BindSampler;

    /// <summary>Null where the context cannot take EGL images.</summary>
    public delegate* unmanaged<int, IntPtr, void> EglImageTargetTexture2D;

    private GlFunctions()
    {
    }

    /// <returns>Null when a function the presenter needs is missing.</returns>
    public static GlFunctions? Load(GlInterface gl)
    {
        var functions = new GlFunctions();
        var missing = false;

        IntPtr Get(string name)
        {
            var address = gl.GetProcAddress(name);
            missing |= address == IntPtr.Zero;
            return address;
        }

        functions.GenTextures = (delegate* unmanaged<int, uint*, void>)Get("glGenTextures");
        functions.DeleteTextures = (delegate* unmanaged<int, uint*, void>)Get("glDeleteTextures");
        functions.BindTexture = (delegate* unmanaged<int, uint, void>)Get("glBindTexture");
        functions.ActiveTexture = (delegate* unmanaged<int, void>)Get("glActiveTexture");
        functions.TexImage2D = (delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void>)Get("glTexImage2D");
        functions.TexSubImage2D = (delegate* unmanaged<int, int, int, int, int, int, int, int, void*, void>)Get("glTexSubImage2D");
        functions.TexParameteri = (delegate* unmanaged<int, int, int, void>)Get("glTexParameteri");
        functions.PixelStorei = (delegate* unmanaged<int, int, void>)Get("glPixelStorei");
        functions.CreateShader = (delegate* unmanaged<int, uint>)Get("glCreateShader");
        functions.ShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Get("glShaderSource");
        functions.CompileShader = (delegate* unmanaged<uint, void>)Get("glCompileShader");
        functions.GetShaderiv = (delegate* unmanaged<uint, int, int*, void>)Get("glGetShaderiv");
        functions.GetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Get("glGetShaderInfoLog");
        functions.DeleteShader = (delegate* unmanaged<uint, void>)Get("glDeleteShader");
        functions.CreateProgram = (delegate* unmanaged<uint>)Get("glCreateProgram");
        functions.AttachShader = (delegate* unmanaged<uint, uint, void>)Get("glAttachShader");
        functions.LinkProgram = (delegate* unmanaged<uint, void>)Get("glLinkProgram");
        functions.GetProgramiv = (delegate* unmanaged<uint, int, int*, void>)Get("glGetProgramiv");
        functions.DeleteProgram = (delegate* unmanaged<uint, void>)Get("glDeleteProgram");
        functions.UseProgram = (delegate* unmanaged<uint, void>)Get("glUseProgram");
        functions.GetUniformLocation = (delegate* unmanaged<uint, byte*, int>)Get("glGetUniformLocation");
        functions.Uniform1i = (delegate* unmanaged<int, int, void>)Get("glUniform1i");
        functions.Uniform3f = (delegate* unmanaged<int, float, float, float, void>)Get("glUniform3f");
        functions.UniformMatrix3fv = (delegate* unmanaged<int, int, byte, float*, void>)Get("glUniformMatrix3fv");
        functions.GenVertexArrays = (delegate* unmanaged<int, uint*, void>)Get("glGenVertexArrays");
        functions.DeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Get("glDeleteVertexArrays");
        functions.BindVertexArray = (delegate* unmanaged<uint, void>)Get("glBindVertexArray");
        functions.DrawArrays = (delegate* unmanaged<int, int, int, void>)Get("glDrawArrays");
        functions.Viewport = (delegate* unmanaged<int, int, int, int, void>)Get("glViewport");
        functions.ClearColor = (delegate* unmanaged<float, float, float, float, void>)Get("glClearColor");
        functions.Clear = (delegate* unmanaged<int, void>)Get("glClear");
        functions.BindFramebuffer = (delegate* unmanaged<int, uint, void>)Get("glBindFramebuffer");
        functions.GenFramebuffers = (delegate* unmanaged<int, uint*, void>)Get("glGenFramebuffers");
        functions.DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Get("glDeleteFramebuffers");
        functions.FramebufferTexture2D = (delegate* unmanaged<int, int, int, uint, int, void>)Get("glFramebufferTexture2D");
        functions.CheckFramebufferStatus = (delegate* unmanaged<int, int>)Get("glCheckFramebufferStatus");
        functions.Disable = (delegate* unmanaged<int, void>)Get("glDisable");
        functions.ColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)Get("glColorMask");
        functions.BindBuffer = (delegate* unmanaged<int, uint, void>)Get("glBindBuffer");

        if (missing)
            return null;

        functions.EglImageTargetTexture2D = (delegate* unmanaged<int, IntPtr, void>)gl.GetProcAddress("glEGLImageTargetTexture2DOES");
        functions.BindSampler = (delegate* unmanaged<uint, uint, void>)gl.GetProcAddress("glBindSampler");

        return functions;
    }

    public uint GenFramebuffer()
    {
        uint framebuffer;
        GenFramebuffers(1, &framebuffer);
        return framebuffer;
    }

    public void DeleteFramebuffer(uint framebuffer)
    {
        if (framebuffer != 0)
            DeleteFramebuffers(1, &framebuffer);
    }

    /// <summary>
    /// Undoes what the renderer of the user interface may have left set and a plain draw over a
    /// whole framebuffer does not want.
    /// </summary>
    public void ResetDrawState()
    {
        Disable(ScissorTest);
        Disable(Blend);
        Disable(DepthTest);
        Disable(StencilTest);
        Disable(CullFace);
        ColorMask(1, 1, 1, 1);
        BindBuffer(PixelUnpackBuffer, 0);
        PixelStorei(UnpackRowLength, 0);

        // Skia samples through sampler objects, which override the parameters of a texture: one left
        // bound with a mipmap filter, after a downscaled image, makes our textures without mipmaps
        // incomplete, and they read as black
        if (BindSampler != null)
        {
            BindSampler(0, 0);
            BindSampler(1, 0);
        }
    }

    public uint GenTexture()
    {
        uint texture;
        GenTextures(1, &texture);
        return texture;
    }

    public void DeleteTexture(uint texture)
    {
        if (texture != 0)
            DeleteTextures(1, &texture);
    }

    /// <summary>A texture that is sampled linearly and never wraps, bound to unit 0.</summary>
    public uint CreateTexture()
    {
        var texture = GenTexture();

        ActiveTexture(Texture0);
        BindTexture(Texture2D, texture);
        TexParameteri(Texture2D, TextureMinFilter, Linear);
        TexParameteri(Texture2D, TextureMagFilter, Linear);
        TexParameteri(Texture2D, TextureWrapS, ClampToEdge);
        TexParameteri(Texture2D, TextureWrapT, ClampToEdge);
        return texture;
    }

    /// <returns>The program, or 0 with the reason in <paramref name="error"/>.</returns>
    public uint BuildProgram(string vertexSource, string fragmentSource, out string? error)
    {
        var vertex = Compile(VertexShader, vertexSource, out error);

        if (vertex == 0)
            return 0;

        var fragment = Compile(FragmentShader, fragmentSource, out error);

        if (fragment == 0)
        {
            DeleteShader(vertex);
            return 0;
        }

        var program = CreateProgram();
        AttachShader(program, vertex);
        AttachShader(program, fragment);
        LinkProgram(program);
        DeleteShader(vertex);
        DeleteShader(fragment);

        int linked;
        GetProgramiv(program, LinkStatus, &linked);

        if (linked == 0)
        {
            DeleteProgram(program);
            error = "link failed";
            return 0;
        }

        error = null;
        return program;
    }

    public int Uniform(uint program, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + "\0");

        fixed (byte* pointer = bytes)
            return GetUniformLocation(program, pointer);
    }

    private uint Compile(int type, string source, out string? error)
    {
        var shader = CreateShader(type);
        var bytes = Encoding.UTF8.GetBytes(source);

        fixed (byte* pointer = bytes)
        {
            var sources = pointer;
            var length = bytes.Length;
            ShaderSource(shader, 1, &sources, &length);
        }

        CompileShader(shader);

        int compiled;
        GetShaderiv(shader, CompileStatus, &compiled);

        if (compiled != 0)
        {
            error = null;
            return shader;
        }

        var log = stackalloc byte[1024];
        int written;
        GetShaderInfoLog(shader, 1024, &written, log);
        error = Marshal.PtrToStringUTF8((IntPtr)log, Math.Clamp(written, 0, 1024));
        DeleteShader(shader);
        return 0;
    }
}

/// <summary>
/// The EGL calls that import DMA-BUF planes, made on the display of the current context.
/// </summary>
internal static unsafe class EglDmaBuf
{
    private const string Library = "libEGL.so.1";

    private const int Extensions = 0x3055;
    private const int Width = 0x3057;
    private const int Height = 0x3056;
    private const int LinuxDmaBuf = 0x3270;
    private const int LinuxDrmFourcc = 0x3271;
    private const int Plane0Fd = 0x3272;
    private const int Plane0Offset = 0x3273;
    private const int Plane0Pitch = 0x3274;
    private const int Plane0ModifierLow = 0x3443;
    private const int Plane0ModifierHigh = 0x3444;
    private const int None = 0x3038;

    // DRM_FORMAT_MOD_INVALID: the layout is implied by the driver
    private const ulong ModifierInvalid = 0x00FFFFFFFFFFFFFF;

    [DllImport(Library)]
    private static extern IntPtr eglGetCurrentDisplay();

    [DllImport(Library)]
    private static extern IntPtr eglQueryString(IntPtr display, int name);

    [DllImport(Library)]
    private static extern IntPtr eglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    private static delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int*, IntPtr> _createImage;
    private static delegate* unmanaged<IntPtr, IntPtr, int> _destroyImage;
    private static bool _isLoaded;
    private static bool _hasModifiers;

    /// <summary>
    /// The display of the current context when it can import DMA-BUF planes, or zero. Call with a
    /// context current; a context that is not an EGL one has no display.
    /// </summary>
    public static IntPtr GetImportingDisplay()
    {
        try
        {
            var display = eglGetCurrentDisplay();

            if (display == IntPtr.Zero)
                return IntPtr.Zero;

            var extensions = Marshal.PtrToStringAnsi(eglQueryString(display, Extensions)) ?? "";

            if (!(" " + extensions + " ").Contains(" EGL_EXT_image_dma_buf_import ", StringComparison.Ordinal))
                return IntPtr.Zero;

            if (!_isLoaded)
            {
                _createImage = (delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, int*, IntPtr>)eglGetProcAddress("eglCreateImageKHR");
                _destroyImage = (delegate* unmanaged<IntPtr, IntPtr, int>)eglGetProcAddress("eglDestroyImageKHR");
                _hasModifiers = extensions.Contains("EGL_EXT_image_dma_buf_import_modifiers", StringComparison.Ordinal);
                _isLoaded = true;
            }

            return _createImage == null || _destroyImage == null ? IntPtr.Zero : display;
        }
        catch (DllNotFoundException)
        {
            return IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>One plane as an image of a single-plane format.</summary>
    /// <returns>Zero when the driver refused it.</returns>
    public static IntPtr CreateImage(IntPtr display, int width, int height, uint fourcc, int fd, uint offset, uint pitch,
        ulong modifier)
    {
        var attributes = stackalloc int[17];
        var count = 0;

        attributes[count++] = Width;
        attributes[count++] = width;
        attributes[count++] = Height;
        attributes[count++] = height;
        attributes[count++] = LinuxDrmFourcc;
        attributes[count++] = (int)fourcc;
        attributes[count++] = Plane0Fd;
        attributes[count++] = fd;
        attributes[count++] = Plane0Offset;
        attributes[count++] = (int)offset;
        attributes[count++] = Plane0Pitch;
        attributes[count++] = (int)pitch;

        if (_hasModifiers && modifier != ModifierInvalid)
        {
            attributes[count++] = Plane0ModifierLow;
            attributes[count++] = (int)(modifier & 0xFFFFFFFF);
            attributes[count++] = Plane0ModifierHigh;
            attributes[count++] = (int)(modifier >> 32);
        }

        attributes[count] = None;

        return _createImage(display, IntPtr.Zero, LinuxDmaBuf, IntPtr.Zero, attributes);
    }

    public static void DestroyImage(IntPtr display, IntPtr image)
    {
        if (image != IntPtr.Zero)
            _destroyImage(display, image);
    }
}
