﻿using GoodAI.Core.Nodes;
using GoodAI.Core.Utils;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.ConstrainedExecution;
using YAXLib;

namespace GoodAI.Core.Observers
{
    [YAXSerializableType(FieldsToSerialize = YAXSerializationFields.AttributedFieldsOnly)]
    public abstract class MyAbstractObserver : IDisposable, IValidatable
    {
        [YAXSerializableField]
        public string Id
        {
            get { return m_id; }
            set
            {
                // Older projects will not have an ID generated for observers, keep the autogenerated one
                if (value == null)
                    return;

                m_id = value;
            }
        }

        #region Core stuff

        private bool m_userResetNeeded = false;

        public void TriggerReset()
        {
            m_userResetNeeded = true;
        }

        public bool ViewResetNeeded { get; set; }

        public void TriggerViewReset()
        {
            ViewResetNeeded = true;
        }

        public uint SimulationStep { get; private set; }

        public bool Initialized { get; set; }
        public bool Active { get; set; }        

        private object m_target;
        public object GenericTarget
        {
            get { return m_target; }
            set
            {
                m_target = value;
                TargetIdentifier = CreateTargetIdentifier();
                OnTargetChanged();
            }
        }

        protected virtual void Reset() { }
        protected abstract void Execute();

        protected MyAbstractObserver()
        {
            KeepRatio = true;
            Shapes = new List<MyShape>();

            // This will be overwritten during deserialization.
            Id = Guid.NewGuid().ToString();
        }

        public virtual void Dispose()
        {
            DeleteTextureVBO();
            DeleteVertexVBO();
        }

        public void UpdateFrame(uint simulationStep)
        {            
            SimulationStep = simulationStep;            

            if (m_userResetNeeded)
            {
                m_userResetNeeded = false;

                Shapes.Clear();

                Reset();

                if (Shapes.Count == 0)
                {
                    Shapes.Add(new MyDefaultShape());
                }

                foreach (MyShape shape in Shapes)
                {
                    shape.Observer = this;
                }
            }

            if (m_textureResetNeeded)
            {
                m_textureResetNeeded = false;

                TextureSize = TextureWidth * TextureHeight;

                DeleteTextureVBO();
                CreateTextureVBO();
                TriggerViewReset();
            }

            if (m_verticesResetNeeded)
            {
                m_verticesResetNeeded = false;
                DeleteVertexVBO();
                CreateVertexVBO();
            }

            if (m_cudaTextureSource != null)
            {
                m_cudaTextureSource.Map();
            }

            if (m_cudaVertexSource != null)
            {
                m_cudaVertexSource.Map();
            }

            try
            {
                PrepareExecution();
                Execute();
            }
            finally
            {
                if (m_cudaTextureSource != null)
                {
                    m_cudaTextureSource.UnMap();
                }
                else if (m_cudaTextureVar != null)
                {
                    CopyCudaVariableToHost(m_cudaTextureVar, BufferTarget.PixelUnpackBuffer, m_textureVBO);
                }

                if (m_cudaVertexSource != null)
                {
                    m_cudaVertexSource.UnMap();
                }
                else if (m_cudaVertexVar != null)
                {
                    CopyCudaVariableToHost(m_cudaVertexVar, BufferTarget.ArrayBuffer, m_vertexVBO);
                }
            }
        }

        private static void CopyCudaVariableToHost<T>(
            CudaDeviceVariable<T> variable, BufferTarget bufferTarget, uint buffer)
            where T : struct
        {
            GL.BindBuffer(bufferTarget, buffer);
            IntPtr ptr = GL.MapBuffer(bufferTarget, BufferAccess.WriteOnly);

            variable.CopyToHost(ptr);

            GL.UnmapBuffer(bufferTarget);
            GL.BindBuffer(bufferTarget, 0);
        }

        protected virtual void PrepareExecution() { }

        #endregion

        #region Render & Window properties

        public enum ViewMethod 
        {
            Fit_2D,
            Free_2D,
            Orbit_3D
        }

        private ViewMethod m_viewMode;
        [MyBrowsable, Category("\tWindow")]
        [YAXSerializableField]
        public ViewMethod ViewMode 
        {
            get { return m_viewMode; }
            set
            {
                m_viewMode = value;
                TriggerViewReset();
            }
        }

        private bool m_keepRatio;
        [MyBrowsable, Category("\tWindow")]
        [YAXSerializableField(DefaultValue = true)]
        public bool KeepRatio 
        {
            get { return m_keepRatio; }
            set 
            { 
                m_keepRatio = value;
                TriggerViewReset();
            } 
        }

        [YAXSerializableField, YAXSerializeAs("Location"), YAXElementFor("Window")]
        public MyLocation WindowLocation { get; set; }

        [YAXSerializableField, YAXSerializeAs("Size"), YAXElementFor("Window")]
        public MySize WindowSize { get; set; }

        [YAXSerializableField, YAXSerializeAs("CameraData"), YAXElementFor("Window")]
        public MyCameraData CameraData { get; set; }

        #endregion

        #region Snapshot

        [YAXSerializableField(DefaultValue = false)]
        public bool AutosaveSnapshop { get; set; }

        public Bitmap GrabObserverTexture()
        {
            if (GraphicsContext.CurrentContext == null)
                throw new GraphicsContextMissingException();

            Bitmap bmp = new Bitmap(TextureWidth, TextureHeight);
            System.Drawing.Imaging.BitmapData data =
                bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            GL.Enable(EnableCap.Texture2D);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, TextureVBO);
            GL.BindTexture(TextureTarget.Texture2D, TextureId);
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);

            bmp.UnlockBits(data);
            return bmp;
        }

        #endregion

        #region Validation
        public virtual void Validate(MyValidator validator) { }

        #endregion

        #region Texture

        private bool m_textureResetNeeded = false;
        private int m_textureWidth;
        private int m_textureHeight;
        
        [MyBrowsable, Category("Texture")]
        public int TextureWidth
        {
            get { return m_textureWidth; }
            protected set
            {
                if (m_textureWidth != value)
                {
                    m_textureResetNeeded = true;
                    m_textureWidth = value;
                }
            }
        }

        [MyBrowsable, Category("Texture")]
        public int TextureHeight
        {
            get { return m_textureHeight; }
            protected set
            {
                if (m_textureHeight != value)
                {
                    m_textureResetNeeded = true;
                    m_textureHeight = value;
                }
            }
        }

        public int TextureSize { get; private set; }

        public bool m_filtering;
        [MyBrowsable, Category("Texture")]
        [YAXSerializableField(DefaultValue = false)]
        public bool BilinearFiltering
        {
            get { return m_filtering; }
            set
            {
                if (m_filtering != value)
                {
                    m_textureResetNeeded = true;
                    m_filtering = value;
                }
            }
        }

        private uint m_texture_id = 0;
        public uint TextureId { get { return m_texture_id; } }

        private uint m_textureVBO = 0;
        public uint TextureVBO { get { return m_textureVBO; } }

        private CudaOpenGLBufferInteropResource m_cudaTextureSource;
        private CudaDeviceVariable<uint> m_cudaTextureVar;

        protected CUdeviceptr VBODevicePointer
        {
            get
            {
                return (m_cudaTextureSource != null) 
                    ? m_cudaTextureSource.GetMappedPointer<uint>().DevicePointer
                    : m_cudaTextureVar.DevicePointer;
            }
        }

        private void CreateTextureVBO()
        {
            if (!Initialized)
                return;

            int length = TextureWidth * TextureHeight * sizeof(uint);
            if (length <= 0)
                return;

            //unbind - just in case this is causing us the invalid exception problems
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            //create buffer
            GL.GenBuffers(1, out m_textureVBO);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, m_textureVBO);
            GL.BufferData(BufferTarget.PixelUnpackBuffer, (IntPtr)(length), IntPtr.Zero, BufferUsageHint.DynamicCopy);  // use data instead of IntPtr.Zero if needed
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

            try
            {
                m_cudaTextureSource = new CudaOpenGLBufferInteropResource(m_textureVBO, CUGraphicsRegisterFlags.None); //.WriteDiscard);  // Write only by CUDA
            }
            catch (CudaException e)
            {
                MyLog.DEBUG.WriteLine(
                    "{0}: CUDA-OpenGL interop error while itializing texture (using fallback): {1}",
                    GetType().Name, e.Message);

                m_cudaTextureVar = new CudaDeviceVariable<uint>(TextureSize);
            }

            // Enable Texturing
            GL.Enable(EnableCap.Texture2D);
            // Generate a texture ID
            GL.GenTextures(1, out m_texture_id);
            // Make this the current texture (remember that GL is state-based)
            GL.BindTexture(TextureTarget.Texture2D, m_texture_id);
            // Allocate the texture memory. The last parameter is NULL since we only
            // want to allocate memory, not initialize it 
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, TextureWidth, TextureHeight, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);

            // Must set the filter mode, GL_LINEAR enables interpolation when scaling 
            int filter = BilinearFiltering
                ? (int)OpenTK.Graphics.OpenGL.All.Linear
                : (int)OpenTK.Graphics.OpenGL.All.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        }

        // unregister this buffer object with CUDA and destroy buffer
        private void DeleteTextureVBO()
        {
            if ((m_cudaTextureSource == null) && (m_cudaTextureVar == null))
                return;

            if (m_cudaTextureSource != null)
            {
                m_cudaTextureSource.Dispose();
                m_cudaTextureSource = null;
            }
            else if (m_cudaTextureVar != null)
            {
                m_cudaTextureVar.Dispose();
                m_cudaTextureVar = null;
            }

            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.DeleteBuffers(1, ref m_textureVBO);

            GL.DeleteTextures(1, ref m_texture_id);
            m_textureVBO = 0;
            m_texture_id = 0;
        }

        // TODO: Define in all derived observers and actually call from somewhere.
        protected virtual void SetDefaultTextureDimensions(int pixelCount)
        {
            Size textureSize = ComputeTextureSize(pixelCount);

            TextureWidth = textureSize.Width;
            TextureHeight = textureSize.Height;
        }

        protected static Size ComputeTextureSize(int pixelCount)
        {
            //TODO: this is not optimal, should be done by prime factorization
            int root = (int) Math.Sqrt(pixelCount);
            int originalRoot = root;

            while (root > originalRoot / 2)
            {
                if (pixelCount % root == 0)
                {
                    return new Size(pixelCount/root, root);
                }

                root--;
            }

            return new Size(pixelCount/root, root + 1);
        }

        #endregion

        #region Vertices

        private bool m_verticesResetNeeded = false;
        private int m_vertexDataSize;        

        [MyBrowsable, Category("Vertices")]
        public int VertexDataSize
        {
            get { return m_vertexDataSize; }
            protected set
            {
                if (m_vertexDataSize != value)
                {
                    m_verticesResetNeeded = true;
                    m_vertexDataSize = value;
                }
            }
        }

        private uint m_vertexVBO = 0;
        public uint VertexVBO { get { return m_vertexVBO; } }

        private CudaOpenGLBufferInteropResource m_cudaVertexSource;
        private CudaDeviceVariable<float> m_cudaVertexVar;

        protected CUdeviceptr VertexVBODevicePointer
        {
            get
            {
                return (m_cudaVertexSource != null)
                    ? m_cudaVertexSource.GetMappedPointer<uint>().DevicePointer
                    : m_cudaVertexVar.DevicePointer;
            }
        }
        
        private void CreateVertexVBO()
        {
            if (!Initialized)
                return;

            int length = VertexDataSize * sizeof(float);

            //unbind - just in case this is causing us the invalid exception problems
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            //create buffer
            GL.GenBuffers(1, out m_vertexVBO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, m_vertexVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(length), IntPtr.Zero, BufferUsageHint.DynamicCopy);  // use data instead of IntPtr.Zero if needed
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            try
            {
                m_cudaVertexSource = new CudaOpenGLBufferInteropResource(m_vertexVBO, CUGraphicsRegisterFlags.None);
            }
            catch (CudaException e)
            {
                MyLog.DEBUG.WriteLine(
                    "{0}: CUDA-OpenGL interop error while itializing vertex data (using fallback): {1}",
                    GetType().Name, e.Message);

                m_cudaVertexVar = new CudaDeviceVariable<float>(VertexDataSize);
            }
        }

        // unregister this buffer object with CUDA and destroy buffer
        private void DeleteVertexVBO()
        {
            if ((m_cudaVertexSource == null) && (m_cudaVertexVar == null))
                return;

            if (m_cudaVertexSource != null)
            {
                m_cudaVertexSource.Dispose();
                m_cudaVertexSource = null;
            }
            else if (m_cudaVertexVar != null)
            {
                m_cudaVertexVar.Dispose();
                m_cudaVertexVar = null;
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffers(1, ref m_vertexVBO);

            m_vertexVBO = 0;
        }

        #endregion

        #region Shapes

        public List<MyShape> Shapes { get; private set; }

        #endregion

        #region Target identification

        public abstract string GetTargetName(MyNode declaredOwner);
        protected abstract string CreateTargetIdentifier();
        public abstract void RestoreTargetFromIdentifier(MyProject project);

        [YAXSerializableField]
        public string TargetIdentifier { get; internal set; }

        public void UpdateTargetIdentifier()
        {
            if (GenericTarget != null)
            {
                TargetIdentifier = CreateTargetIdentifier();
            }
        }

        public string Name 
        { 
            get 
            { 
                return TargetIdentifier + ": " + MyProject.ShortenNodeTypeName(GetType());
            } 
        }

        #endregion

        #region Events

        public event PropertyChangedEventHandler RuntimePropertyChanged;

        protected void OnRuntimePropertyChanged()
        {
            if (RuntimePropertyChanged != null)
            {
                RuntimePropertyChanged(this, new PropertyChangedEventArgs("RuntimeProperty"));
            }
        }

        public event PropertyChangedEventHandler TargetChanged;

        private void OnTargetChanged()
        {
            if (TargetChanged != null)
            {
                TargetChanged(GenericTarget, new PropertyChangedEventArgs("Target"));
            }
        }

        #endregion

        #region Kernel stuff

        protected MyCudaKernel m_kernel;
        private string m_id;

        public int ObserverGPU
        {
            //TODO: replace this with code for finding GPU with connected display
            get { return MyKernelFactory.Instance.DevCount - 1; }
        }

        #endregion         
    }

    public abstract class MyObserver<T> : MyAbstractObserver
    {
        public T Target 
        { 
            get { return (T)GenericTarget; }
            set { GenericTarget = value; }
        }        
    }

    [YAXSerializeAs("NodeObserver")]
    public abstract class MyNodeObserver<T> : MyObserver<T> where T : MyWorkingNode
    {
        public override string GetTargetName(MyNode declaredOwner)
        {
            return Target.Name;
        }

        protected override string CreateTargetIdentifier()
        {
            return Target != null ? Target.Id.ToString() : String.Empty;
        }

        public override void RestoreTargetFromIdentifier(MyProject project)
        {
            Target = (T)project.GetNodeById(int.Parse(TargetIdentifier));
        }
    }
}
