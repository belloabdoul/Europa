using System.Collections;
using Core.Entities.Audios;

namespace Api.Client.Repositories;

public interface IAudioInfosRepository
{
    ValueTask<BitArray?> GetAudioInfos(byte[] id);

    ValueTask<bool> InsertAudioInfos(AudioGroups group);
}