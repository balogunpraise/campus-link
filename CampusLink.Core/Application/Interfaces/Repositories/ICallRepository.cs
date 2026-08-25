using CampusLink.Core.Domain.Entities;

namespace CampusLink.Core.Application.Interfaces.Repositories;

public interface ICallRepository : IRepositoryBase<Call>
{
    // The live call in a conversation, if there is one. A conversation holds at
    // most one at a time, which is what makes "join the call" unambiguous.
    Task<Call> GetActiveForConversationAsync(
        Guid conversationId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    Task<Call> GetWithParticipantsAsync(
        Guid callId,
        bool trackChanges,
        CancellationToken cancellationToken = default);

    IQueryable<Call> GetHistory(Guid conversationId, bool trackChanges);

    // Ringing calls older than the cut-off, for the no-answer sweep.
    IQueryable<Call> GetStaleRinging(DateTime startedBeforeUtc, bool trackChanges);

    Task<CallParticipant> GetParticipantAsync(
        Guid callId,
        Guid userId,
        bool trackChanges,
        CancellationToken cancellationToken = default);
}
