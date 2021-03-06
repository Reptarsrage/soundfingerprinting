﻿namespace SoundFingerprinting.FFT
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using SoundFingerprinting.Audio;
    using SoundFingerprinting.Configuration;
    using SoundFingerprinting.Data;

    internal class SpectrumService : ISpectrumService
    {
        private readonly IFFTService fftService;
        private readonly ILogUtility logUtility;

        internal SpectrumService(IFFTService fftService, ILogUtility logUtility)
        {
            this.fftService = fftService;
            this.logUtility = logUtility;
        }

        public float[][] CreateSpectrogram(AudioSamples audioSamples, int overlap, int wdftSize)
        {
            float[] window = new DefaultSpectrogramConfig().Window.GetWindow(wdftSize);
            float[] samples = audioSamples.Samples;
            int width = (samples.Length - wdftSize) / overlap;
            float[][] frames = new float[width][];
            for (int i = 0; i < width; i++)
            {
                float[] complexSignal = fftService.FFTForward(samples, i * overlap, wdftSize, window);
                float[] band = new float[(wdftSize / 2) + 1];
                for (int j = 0; j < wdftSize / 2; j++)
                {
                    double re = complexSignal[2 * j];
                    double img = complexSignal[(2 * j) + 1];

                    re /= (float)wdftSize / 2;
                    img /= (float)wdftSize / 2;

                    band[j] = (float)((re * re) + (img * img));
                }

                frames[i] = band;
            }

            return frames;
        }

        public List<SpectralImage> CreateLogSpectrogram(AudioSamples audioSamples, SpectrogramConfig configuration)
        {
            int wdftSize = configuration.WdftSize;
            int width = (audioSamples.Samples.Length - wdftSize) / configuration.Overlap;
            if (width < 1)
            {
                return new List<SpectralImage>();
            }

            float[] frames = new float[width * configuration.LogBins];
            ushort[] logFrequenciesIndexes = logUtility.GenerateLogFrequenciesRanges(audioSamples.SampleRate, configuration);
            float[] window = configuration.Window.GetWindow(wdftSize);
            float[] samples = audioSamples.Samples;
            Parallel.For(0, width, 
                () => new float[wdftSize],
                (index, loop, fftArray) =>
                {
                    CopyAndWindow(fftArray, samples, index * configuration.Overlap, window);
                    fftService.FFTForwardInPlace(fftArray);
                    ExtractLogBins(fftArray, logFrequenciesIndexes, configuration.LogBins, wdftSize, frames, index);
                    return fftArray;
                },
                fftArray => { });

            return CutLogarithmizedSpectrum(frames, audioSamples.SampleRate, configuration);
        }

        public List<SpectralImage> CutLogarithmizedSpectrum(float[] logarithmizedSpectrum, int sampleRate, SpectrogramConfig configuration)
        {
            var strideBetweenConsecutiveImages = configuration.Stride;
            int overlap = configuration.Overlap;
            int index = GetFrequencyIndexLocationOfAudioSamples(strideBetweenConsecutiveImages.FirstStride, overlap);
            int numberOfLogBins = configuration.LogBins;
            var spectralImages = new List<SpectralImage>();

            int width = logarithmizedSpectrum.Length / numberOfLogBins;
            ushort fingerprintImageLength = configuration.ImageLength;
            int fullLength = configuration.ImageLength * numberOfLogBins;
            uint sequenceNumber = 0;
            while (index + fingerprintImageLength <= width)
            {
                float[] spectralImage = new float[fingerprintImageLength * numberOfLogBins]; 
                Buffer.BlockCopy(logarithmizedSpectrum, sizeof(float) * index * numberOfLogBins, spectralImage,  0, fullLength * sizeof(float));
                float startsAt = index * ((float)overlap / sampleRate);
                spectralImages.Add(new SpectralImage(spectralImage, fingerprintImageLength, (ushort)numberOfLogBins, startsAt, sequenceNumber));
                index += fingerprintImageLength + GetFrequencyIndexLocationOfAudioSamples(strideBetweenConsecutiveImages.NextStride, overlap);
                sequenceNumber++;
            }

            return spectralImages;
        }

        public void ExtractLogBins(float[] spectrum, ushort[] logFrequenciesIndex, int logBins, int wdftSize, float[] targetArray, int targetIndex)
        {
            int width = wdftSize / 2; /* 1024 */
            for (int i = 0; i < logBins; i++)
            {
                int lowBound = logFrequenciesIndex[i];
                int higherBound = logFrequenciesIndex[i + 1];

                for (int k = lowBound; k < higherBound; k++)
                {
                    double re = spectrum[2 * k] / width;
                    double img = spectrum[(2 * k) + 1] / width;
                    targetArray[(targetIndex * logBins) + i] += (float)((re * re) + (img * img));
                }

                targetArray[(targetIndex * logBins) + i] /= (higherBound - lowBound);
            }
        }

        private int GetFrequencyIndexLocationOfAudioSamples(int audioSamples, int overlap)
        {
            // There are 64 audio samples in 1 unit of spectrum due to FFT window overlap (which is 64)
            return (int)((float)audioSamples / overlap);
        }

        private void CopyAndWindow(float[] fftArray, float[] samples, int prefix, float[] window)
        {
            for (int j = 0; j < window.Length; ++j)
            {
                fftArray[j] = samples[prefix + j] * window[j];
            }
        }
    }
}