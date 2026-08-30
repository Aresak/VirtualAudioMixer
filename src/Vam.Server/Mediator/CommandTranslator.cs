using Vam.Protocol.V1;
using Vam.Server.Mediator.Contracts;

namespace Vam.Server.Mediator;

/// <summary>
/// Turns one wire command into the contract it stands for.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam the transport decision asks for, in one direction: <b>the contracts define the
/// operations and the wire format serialises them</b>, rather than the other way round. A second
/// transport — the WebSocket adapter G2a wants — writes the other half of this file and nothing
/// else, because everything below it is already transport-agnostic.
/// </para>
/// <para>
/// It is a translation and deliberately nothing more. No validation, no engine access, no decisions:
/// a mapping with a rule in it is a rule that exists in two places, and the second one is the one
/// nobody updates.
/// </para>
/// </remarks>
public static class CommandTranslator
{
    /// <summary>Translates a wire command.</summary>
    /// <param name="command">What arrived.</param>
    /// <returns>The contract, or null when the command carried nothing this build knows.</returns>
    public static object? Translate(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.KindCase switch
        {
            Command.KindOneofCase.SetFader =>
                new SetFaderRequest(command.SetFader.ChannelIndex, command.SetFader.Decibels),

            Command.KindOneofCase.SetTrim =>
                new SetTrimRequest(command.SetTrim.ChannelIndex, command.SetTrim.Decibels),

            Command.KindOneofCase.SetPan =>
                new SetPanRequest(command.SetPan.ChannelIndex, command.SetPan.Pan),

            Command.KindOneofCase.SetFlag =>
                new SetChannelFlagRequest(command.SetFlag.ChannelIndex, command.SetFlag.Flag, command.SetFlag.Enabled),

            Command.KindOneofCase.SetAutomixWeight =>
                new SetAutomixWeightRequest(command.SetAutomixWeight.ChannelIndex, command.SetAutomixWeight.Weight),

            Command.KindOneofCase.SetChannelName =>
                new SetChannelNameRequest(command.SetChannelName.ChannelIndex, command.SetChannelName.Name),

            Command.KindOneofCase.SetChannelColour =>
                new SetChannelColourRequest(command.SetChannelColour.ChannelIndex, command.SetChannelColour.Colour),

            Command.KindOneofCase.SetChannelDevice =>
                new SetChannelDeviceRequest(command.SetChannelDevice.ChannelIndex, command.SetChannelDevice.DeviceId),

            Command.KindOneofCase.AddChannel => new AddChannelRequest(
                command.AddChannel.Name,
                command.AddChannel.DeviceId,
                command.AddChannel.ChannelCount,
                command.AddChannel.ParticipatesInAutomix),

            Command.KindOneofCase.RemoveChannel =>
                new RemoveChannelRequest(command.RemoveChannel.ChannelIndex),

            Command.KindOneofCase.MoveChannel =>
                new MoveChannelRequest(command.MoveChannel.FromIndex, command.MoveChannel.ToIndex),

            Command.KindOneofCase.SetSend => new SetSendRequest(
                command.SetSend.ChannelIndex,
                command.SetSend.BusIndex,
                command.SetSend.On,
                command.SetSend.Decibels),

            Command.KindOneofCase.SetBusGain =>
                new SetBusGainRequest(command.SetBusGain.BusIndex, command.SetBusGain.Decibels),

            Command.KindOneofCase.SetBusMuted =>
                new SetBusMutedRequest(command.SetBusMuted.BusIndex, command.SetBusMuted.Muted),

            Command.KindOneofCase.SetBusName =>
                new SetBusNameRequest(command.SetBusName.BusIndex, command.SetBusName.Name),

            Command.KindOneofCase.SetBusColour =>
                new SetBusColourRequest(command.SetBusColour.BusIndex, command.SetBusColour.Colour),

            Command.KindOneofCase.SetBusRole =>
                new SetBusRoleRequest(command.SetBusRole.BusIndex, command.SetBusRole.Role),

            Command.KindOneofCase.SetBusOutputDevice =>
                new SetBusOutputDeviceRequest(command.SetBusOutputDevice.BusIndex, command.SetBusOutputDevice.DeviceId),

            Command.KindOneofCase.AddBus => new AddBusRequest(
                command.AddBus.Name,
                command.AddBus.Role,
                command.AddBus.ChannelCount,
                command.AddBus.OutputDeviceId),

            Command.KindOneofCase.RemoveBus =>
                new RemoveBusRequest(command.RemoveBus.BusIndex),

            Command.KindOneofCase.AddModifier => new AddModifierRequest(
                command.AddModifier.Target,
                command.AddModifier.ModifierId,
                command.AddModifier.AtIndex),

            Command.KindOneofCase.RemoveModifier =>
                new RemoveModifierRequest(command.RemoveModifier.Target, command.RemoveModifier.LinkIndex),

            Command.KindOneofCase.MoveModifier => new MoveModifierRequest(
                command.MoveModifier.Target,
                command.MoveModifier.FromIndex,
                command.MoveModifier.ToIndex),

            Command.KindOneofCase.SetModifierBypass => new SetModifierBypassRequest(
                command.SetModifierBypass.Target,
                command.SetModifierBypass.LinkIndex,
                command.SetModifierBypass.Bypassed),

            Command.KindOneofCase.SetModifierParameter => new SetModifierParameterRequest(
                command.SetModifierParameter.Target,
                command.SetModifierParameter.LinkIndex,
                command.SetModifierParameter.ParameterId,
                command.SetModifierParameter.Value),

            Command.KindOneofCase.SaveChainPreset =>
                new SaveChainPresetRequest(command.SaveChainPreset.Target, command.SaveChainPreset.Name),

            Command.KindOneofCase.ApplyChainPreset =>
                new ApplyChainPresetRequest(command.ApplyChainPreset.Target, command.ApplyChainPreset.Name),

            Command.KindOneofCase.DeleteChainPreset =>
                new DeleteChainPresetRequest(command.DeleteChainPreset.Name),

            Command.KindOneofCase.SetAutomix => new SetAutomixRequest(
                command.SetAutomix.Bypassed,
                command.SetAutomix.DepthDb,
                command.SetAutomix.ResponseMs),

            Command.KindOneofCase.SetRecording =>
                new SetRecordingRequest(command.SetRecording.Recording, command.SetRecording.Directory),

            Command.KindOneofCase.SetStartupOptions => new SetStartupOptionsRequest(
                command.SetStartupOptions.LoadLastConsole,
                command.SetStartupOptions.RecordAutomatically),

            Command.KindOneofCase.ClearClip =>
                new ClearClipRequest(command.ClearClip.ChannelIndex),

            _ => null
        };
    }
}
