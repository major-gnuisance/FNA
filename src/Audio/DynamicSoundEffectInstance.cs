#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2018 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#endregion

namespace Microsoft.Xna.Framework.Audio
{
	// http://msdn.microsoft.com/en-us/library/microsoft.xna.framework.audio.dynamicsoundeffectinstance.aspx
	public sealed class DynamicSoundEffectInstance : SoundEffectInstance
	{
		#region Public Properties

		public int PendingBufferCount
		{
			get
			{
				return queuedBuffers.Count;
			}
		}

		public override bool IsLooped
		{
			get
			{
				return false;
			}
			set
			{
				// No-op, DynamicSoundEffectInstance cannot be looped!
			}
		}

		#endregion

		#region Internal Variables

		internal FAudio.FAudioWaveFormatEx format;

		#endregion

		#region Private Variables

		private int sampleRate;
		private AudioChannels channels;

		private List<IntPtr> queuedBuffers;
		private List<uint> queuedSizes;

		private FAudio.OnBufferEndFunc OnBufferEndFunc;

		private const int MINIMUM_BUFFER_CHECK = 3;

		#endregion

		#region BufferNeeded Event

		public event EventHandler<EventArgs> BufferNeeded;

		#endregion

		#region Public Constructor

		public DynamicSoundEffectInstance(int sampleRate, AudioChannels channels) : base(null)
		{
			this.sampleRate = sampleRate;
			this.channels = channels;
			isDynamic = true;

			format = new FAudio.FAudioWaveFormatEx();
			format.wFormatTag = 1;
			format.nChannels = (ushort) channels;
			format.nSamplesPerSec = (uint) sampleRate;
			format.wBitsPerSample = 16;

			queuedBuffers = new List<IntPtr>();
			queuedSizes = new List<uint>();

			OnBufferEndFunc = OnBufferEnd;
			unsafe
			{
				FAudio.FAudioVoiceCallback* cb = (FAudio.FAudioVoiceCallback*) callbacks;
				cb->OnBufferEnd = Marshal.GetFunctionPointerForDelegate(OnBufferEndFunc);
			}
		}

		#endregion

		#region Destructor

		~DynamicSoundEffectInstance()
		{
			Dispose();
		}

		#endregion

		#region Public Methods

		public TimeSpan GetSampleDuration(int sizeInBytes)
		{
			return SoundEffect.GetSampleDuration(
				sizeInBytes,
				sampleRate,
				channels
			);
		}

		public int GetSampleSizeInBytes(TimeSpan duration)
		{
			return SoundEffect.GetSampleSizeInBytes(
				duration,
				sampleRate,
				channels
			);
		}

		public override void Play()
		{
			// Wait! What if we need moar buffers?
			Update();

			// Okay we're good
			base.Play();
			FrameworkDispatcher.Streams.Add(this);
		}

		public void SubmitBuffer(byte[] buffer)
		{
			this.SubmitBuffer(buffer, 0, buffer.Length);
		}

		public void SubmitBuffer(byte[] buffer, int offset, int count)
		{
			IntPtr next = Marshal.AllocHGlobal(count);
			Marshal.Copy(buffer, offset, next, count);
			queuedBuffers.Add(next);
			if (State == SoundState.Playing)
			{
				FAudio.FAudioBuffer buf = new FAudio.FAudioBuffer();
				buf.AudioBytes = (uint) count;
				buf.pAudioData = next;
				buf.PlayLength = (
					buf.AudioBytes /
					(uint) channels /
					(uint) (format.wBitsPerSample / 8)
				);
				FAudio.FAudioSourceVoice_SubmitSourceBuffer(
					handle,
					ref buf,
					IntPtr.Zero
				);
			}
			else
			{
				queuedSizes.Add((uint) count);
			}
		}

		public void SubmitFloatBufferEXT(float[] buffer)
		{
			SubmitFloatBufferEXT(buffer, 0, buffer.Length);
		}

		public void SubmitFloatBufferEXT(float[] buffer, int offset, int count)
		{
			/* Float samples are the typical format received from decoders.
			 * We currently use this for the VideoPlayer.
			 * -flibit
			 */
			if (State == SoundState.Playing && format.wBitsPerSample == 16)
			{
				throw new InvalidOperationException(
					"Submit a float buffer before Playing!"
				);
			}
			format.wBitsPerSample = 32;

			IntPtr next = Marshal.AllocHGlobal(count * 4);
			Marshal.Copy(buffer, offset, next, count);
			queuedBuffers.Add(next);
			if (State == SoundState.Playing)
			{
				FAudio.FAudioBuffer buf = new FAudio.FAudioBuffer();
				buf.AudioBytes = (uint) count * 4;
				buf.pAudioData = next;
				buf.PlayLength = (
					buf.AudioBytes /
					(uint) channels /
					(uint) (format.wBitsPerSample / 8)
				);
				FAudio.FAudioSourceVoice_SubmitSourceBuffer(
					handle,
					ref buf,
					IntPtr.Zero
				);
			}
			else
			{
				queuedSizes.Add((uint) count * 4);
			}
		}

		#endregion

		#region Protected Methods

		protected override void Dispose(bool disposing)
		{
			// Not much to see here...
			base.Dispose(disposing);
		}

		#endregion

		#region Internal Methods

		internal void QueueInitialBuffers()
		{
			FAudio.FAudioBuffer buffer = new FAudio.FAudioBuffer();
			for (int i = 0; i < queuedBuffers.Count; i += 1)
			{
				buffer.AudioBytes = queuedSizes[i];
				buffer.pAudioData = queuedBuffers[i];
				buffer.PlayLength = (
					buffer.AudioBytes /
					(uint) channels /
					(uint) (format.wBitsPerSample / 8)
				);
				FAudio.FAudioSourceVoice_SubmitSourceBuffer(
					handle,
					ref buffer,
					IntPtr.Zero
				);
			}
			queuedSizes.Clear();
		}

		internal void ClearBuffers()
		{
			foreach (IntPtr buf in queuedBuffers)
			{
				Marshal.FreeHGlobal(buf);
			}
			queuedBuffers.Clear();
			queuedSizes.Clear();
		}

		internal void Update()
		{
			// Do we need even moar buffers?
			for (
				int i = MINIMUM_BUFFER_CHECK - PendingBufferCount;
				(i > 0) && BufferNeeded != null;
				i -= 1
			) {
				BufferNeeded(this, null);
			}
		}

		#endregion

		#region Private Methods

		private void OnBufferEnd(IntPtr callback, IntPtr pBufferContext)
		{
			Marshal.FreeHGlobal(queuedBuffers[0]);
			queuedBuffers.RemoveAt(0);
		}

		#endregion
	}
}
