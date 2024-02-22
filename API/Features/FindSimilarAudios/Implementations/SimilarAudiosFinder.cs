﻿using API.Common.Entities;
using API.Common.Interfaces;
using API.Features.FindSimilarAudios.Interfaces;
using SoundFingerprinting;
using SoundFingerprinting.Emy;
using SoundFingerprinting.Media;
using System;
using System.Collections.Concurrent;
using File = API.Common.Entities.File;

namespace API.Features.FindSimilarAudios.Implementations
{
    public class SimilarAudiosFinder : ISimilarAudiosFinder
    {
        private readonly IFileTypeIdentifier _fileTypeIdentifier;
        private readonly IModelService _modelService;
        private readonly IMediaService _mediaService;
        private readonly IAudioHashGenerator _audioHashGenerator;
        private readonly object readLock = new();

        public SimilarAudiosFinder(IFileTypeIdentifier fileTypeIdentifier, IModelService modelService, IMediaService mediaService, IAudioHashGenerator audioHashGenerator)
        {
            _fileTypeIdentifier = fileTypeIdentifier;
            _modelService = modelService;
            _mediaService = mediaService;
            _audioHashGenerator = audioHashGenerator;
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicatedAudios = new ConcurrentBag<File>();

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            };

            token.ThrowIfCancellationRequested();

            hypotheticalDuplicates = hypotheticalDuplicates.Where(file => _fileTypeIdentifier.GetFileType(file).Equals("audio")).ToList();

            //await hypotheticalDuplicates.ParallelForEachAsync(async file =>
            //{
            //    try
            //    {
            //        await _audioHashGenerator.GenerateAudioHashes(file, _modelService, _mediaService);
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine(ex.Message);
            //        //duplicatedMusics.Add(new FileDto(new FileInfo(file), _hashGenerator.GenerateHash(file)));
            //    }
            //}, Environment.ProcessorCount);

            Parallel.For(0, hypotheticalDuplicates.Count, options, current =>
            {
                lock (readLock)
                {
                    try
                    {
                        var match = _audioHashGenerator.GetAudioMatches(hypotheticalDuplicates[current], _modelService, _mediaService);
                        if (string.IsNullOrEmpty(match))
                        {
                            _audioHashGenerator.GenerateAudioHashes(hypotheticalDuplicates[current], _modelService, _mediaService);
                        }
                        else
                        {
                            var hash = _audioHashGenerator.GenerateHash(match);
                            if (!duplicatedAudios.Any(audio => audio.Path.Equals(match)))
                            {
                                duplicatedAudios.Add(new File(new FileInfo(match), hash));
                            }
                            duplicatedAudios.Add(new File(new FileInfo(hypotheticalDuplicates[current]), hash));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        //duplicatedMusics.Add(new FileDto(new FileInfo(file), _hashGenerator.GenerateHash(file)));
                    }
                }

            });

            token.ThrowIfCancellationRequested();

            Console.WriteLine($"{duplicatedAudios.Count} audios.");

            //IHashProcessor hashProcessor = new HashProcessor();

            // Here we process hashes to find similar ones.

            //duplicatedImages = await Task.Run(() => hashProcessor.ProcessSimilarImageHashes(hashesWithAssociatedImages, token));

            token.ThrowIfCancellationRequested();
            return [.. duplicatedAudios.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1)];
        }
    }
}