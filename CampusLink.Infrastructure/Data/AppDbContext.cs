using CampusLink.Core.Domain.Entities;
using CampusLink.Core.Domain.Entities.LinkingEntities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CampusLink.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<InstitutionEmailDomain> InstitutionEmailDomains => Set<InstitutionEmailDomain>();
    public DbSet<InstitutionPartnership> InstitutionPartnerships => Set<InstitutionPartnership>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<UserCourse> UserCourses => Set<UserCourse>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceShareRequest> ResourceShareRequests => Set<ResourceShareRequest>();
    public DbSet<StudyBuddyProfile> StudyBuddyProfiles => Set<StudyBuddyProfile>();
    public DbSet<StudyBuddySubject> StudyBuddySubjects => Set<StudyBuddySubject>();
    public DbSet<StudyBuddyLanguage> StudyBuddyLanguages => Set<StudyBuddyLanguage>();
    public DbSet<StudyBuddyMatch> StudyBuddyMatches => Set<StudyBuddyMatch>();
    public DbSet<StudyGroup> StudyGroups => Set<StudyGroup>();
    public DbSet<StudyGroupMember> StudyGroupMembers => Set<StudyGroupMember>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Call> Calls => Set<Call>();
    public DbSet<CallParticipant> CallParticipants => Set<CallParticipant>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // wires up Identity's own tables first

        // Fuzzy institution search: "unilag" should find "University of Lagos"
        // and "Massachusets" should find "Massachusetts". Typos are what create
        // duplicate schools, so the search has to forgive them.
        builder.HasPostgresExtension("pg_trgm");

        // ----- Institution -----
        builder.Entity<Institution>()
            .HasIndex(i => i.Slug)
            .IsUnique();

        // Country-scoped browse, the common case at sign-up.
        builder.Entity<Institution>()
            .HasIndex(i => new { i.CountryCode, i.Name });

        // GIN + trigram ops is what makes ILIKE '%term%' and similarity()
        // index-backed instead of a full scan of every school on the platform.
        builder.Entity<Institution>()
            .HasIndex(i => i.Name)
            .HasDatabaseName("IX_Institutions_Name_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Entity<Institution>()
            .HasIndex(i => i.ShortName)
            .HasDatabaseName("IX_Institutions_ShortName_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // Moderation queue: schools students typed in themselves.
        builder.Entity<Institution>()
            .HasIndex(i => new { i.IsVerified, i.IsUserSubmitted });

        builder.Entity<InstitutionEmailDomain>()
            .HasOne(d => d.Institution)
            .WithMany(i => i.EmailDomains)
            .HasForeignKey(d => d.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);

        // A domain can only ever map to one school — this is the whole basis of
        // sign-up verification.
        builder.Entity<InstitutionEmailDomain>()
            .HasIndex(d => d.Domain)
            .IsUnique();

        // Institution search also matches domains, because that's where the
        // acronyms students type actually live ("unilag" -> unilag.edu.ng).
        // The unique btree above can't serve a leading-wildcard ILIKE.
        // Named overload, otherwise EF treats this as the same index as the
        // unique one above (indexes are keyed by property set) and tries to
        // make that one GIN — which Postgres rejects for unique indexes.
        builder.Entity<InstitutionEmailDomain>()
            .HasIndex(d => d.Domain, "IX_InstitutionEmailDomains_Domain_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // ----- InstitutionPartnership -----
        builder.Entity<InstitutionPartnership>()
            .HasOne(p => p.InstitutionA)
            .WithMany()
            .HasForeignKey(p => p.InstitutionAId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<InstitutionPartnership>()
            .HasOne(p => p.InstitutionB)
            .WithMany()
            .HasForeignKey(p => p.InstitutionBId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pairs are stored normalized (smaller Guid first), so one unique index
        // is enough to stop duplicates in either direction.
        builder.Entity<InstitutionPartnership>()
            .HasIndex(p => new { p.InstitutionAId, p.InstitutionBId })
            .IsUnique();

        builder.Entity<InstitutionPartnership>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_InstitutionPartnership_Ordered",
                "\"InstitutionAId\" < \"InstitutionBId\""));

        // ----- Subject (shared cross-institution taxonomy) -----
        builder.Entity<Subject>()
            .HasIndex(s => s.Slug)
            .IsUnique();

        // ----- AppUser -----
        builder.Entity<AppUser>()
            .HasOne(u => u.Institution)
            .WithMany(i => i.Members)
            .HasForeignKey(u => u.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict); // never wipe out students with a school

        // Student numbers are only unique inside a school.
        builder.Entity<AppUser>()
            .HasIndex(u => new { u.InstitutionId, u.StudentNumber })
            .IsUnique();

        builder.Entity<AppUser>()
            .HasIndex(u => new { u.InstitutionId, u.MembershipStatus });

        // Phone number is a login identifier alongside username and email, so
        // it has to be unique — but only where one was given, hence the filter.
        builder.Entity<AppUser>()
            .HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("\"PhoneNumber\" IS NOT NULL");

        // People search is typo-tolerant across names, so it needs the same
        // trigram treatment as the institution directory — otherwise every
        // search scans every student on the platform.
        builder.Entity<AppUser>()
            .HasIndex(u => u.FirstName, "IX_AspNetUsers_FirstName_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Entity<AppUser>()
            .HasIndex(u => u.LastName, "IX_AspNetUsers_LastName_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // Named overload again: the unique username index already covers this
        // column, and a GIN index can't be unique.
        builder.Entity<AppUser>()
            .HasIndex(u => u.UserName, "IX_AspNetUsers_UserName_Trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        // ----- RefreshToken -----
        builder.Entity<RefreshToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade); // deleting a user kills their sessions

        // Every lookup is by hash, and two tokens must never share one.
        builder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        // "Revoke everything for this user" and the expiry sweep.
        builder.Entity<RefreshToken>()
            .HasIndex(t => new { t.UserId, t.ExpiresAt });

        // ----- Course / UserCourse (many-to-many) -----
        builder.Entity<Course>()
            .HasOne(c => c.Institution)
            .WithMany(i => i.Courses)
            .HasForeignKey(c => c.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Course>()
            .HasOne(c => c.Subject)
            .WithMany(s => s.Courses)
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        // Course codes collide across schools (CSC201 exists everywhere), so
        // uniqueness is scoped to the institution.
        builder.Entity<Course>()
            .HasIndex(c => new { c.InstitutionId, c.Code })
            .IsUnique();

        builder.Entity<Course>()
            .HasIndex(c => c.SubjectId);

        builder.Entity<UserCourse>()
            .HasKey(uc => new { uc.UserId, uc.CourseId });

        builder.Entity<UserCourse>()
            .HasOne(uc => uc.User)
            .WithMany(u => u.EnrolledCourses)
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserCourse>()
            .HasOne(uc => uc.Course)
            .WithMany(c => c.Enrollments)
            .HasForeignKey(uc => uc.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        // ----- Resource -----
        builder.Entity<Resource>()
            .HasOne(r => r.Owner)
            .WithMany(u => u.OwnedResources)
            .HasForeignKey(r => r.OwnerId)
            .OnDelete(DeleteBehavior.Cascade); // owner deleted -> their listings go too

        builder.Entity<Resource>()
            .HasOne(r => r.Institution)
            .WithMany()
            .HasForeignKey(r => r.InstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Resource>()
            .HasOne(r => r.Course)
            .WithMany(c => c.Resources)
            .HasForeignKey(r => r.CourseId)
            .OnDelete(DeleteBehavior.SetNull); // course removed -> resource just becomes uncategorized

        builder.Entity<Resource>()
            .HasOne(r => r.Subject)
            .WithMany(s => s.Resources)
            .HasForeignKey(r => r.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Resource>()
            .HasIndex(r => new { r.CourseId, r.IsAvailable }); // fast "browse available resources for a course"

        // Same-school browse: everything my institution has posted.
        builder.Entity<Resource>()
            .HasIndex(r => new { r.InstitutionId, r.IsAvailable, r.Visibility });

        // Cross-school browse: everything on a subject that isn't
        // institution-only.
        builder.Entity<Resource>()
            .HasIndex(r => new { r.SubjectId, r.Visibility, r.IsAvailable });

        // ----- ResourceShareRequest -----
        builder.Entity<ResourceShareRequest>()
            .HasOne(sr => sr.Resource)
            .WithMany(r => r.ShareRequests)
            .HasForeignKey(sr => sr.ResourceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ResourceShareRequest>()
            .HasOne(sr => sr.Requester)
            .WithMany(u => u.ShareRequestsMade)
            .HasForeignKey(sr => sr.RequesterId)
            .OnDelete(DeleteBehavior.Restrict); // avoid multiple cascade paths back to AppUser

        builder.Entity<ResourceShareRequest>()
            .HasIndex(sr => new { sr.RequesterId, sr.Status });

        // The overdue sweep and the "nudge the other party" job both scan on
        // status + due date.
        builder.Entity<ResourceShareRequest>()
            .HasIndex(sr => new { sr.Status, sr.DueDate });

        // A return can't be recorded before both sides confirmed the handover —
        // enforced in the database too, not just in the entity, so no future
        // service or bulk update can close a loan that never started.
        builder.Entity<ResourceShareRequest>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_ShareRequest_ReturnAfterHandover",
                """
                ("BorrowerConfirmedReturnAt" IS NULL AND "OwnerConfirmedReturnAt" IS NULL)
                OR ("OwnerConfirmedHandoverAt" IS NOT NULL AND "BorrowerConfirmedReceiptAt" IS NOT NULL)
                """));

        // ReturnedAt is the closed-loan marker, so it may only be set once both
        // parties have confirmed the return.
        builder.Entity<ResourceShareRequest>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_ShareRequest_ReturnedAtRequiresBothConfirmations",
                """
                "ReturnedAt" IS NULL
                OR ("BorrowerConfirmedReturnAt" IS NOT NULL AND "OwnerConfirmedReturnAt" IS NOT NULL)
                """));

        // ----- StudyBuddyProfile (1:1 with AppUser) -----
        builder.Entity<StudyBuddyProfile>()
            .HasOne(p => p.User)
            .WithOne(u => u.StudyBuddyProfile)
            .HasForeignKey<StudyBuddyProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudyBuddyProfile>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        // Candidate pool for the matcher: active profiles open beyond their own
        // school.
        builder.Entity<StudyBuddyProfile>()
            .HasIndex(p => new { p.IsActive, p.MatchScope });

        builder.Entity<StudyBuddySubject>()
            .HasKey(s => new { s.StudyBuddyProfileId, s.SubjectId });

        builder.Entity<StudyBuddySubject>()
            .HasOne(s => s.StudyBuddyProfile)
            .WithMany(p => p.PreferredSubjects)
            .HasForeignKey(s => s.StudyBuddyProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudyBuddySubject>()
            .HasOne(s => s.Subject)
            .WithMany()
            .HasForeignKey(s => s.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudyBuddySubject>()
            .HasOne(s => s.Course)
            .WithMany()
            .HasForeignKey(s => s.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        // Drives "who else wants a buddy for this subject", across all schools.
        builder.Entity<StudyBuddySubject>()
            .HasIndex(s => s.SubjectId);

        builder.Entity<StudyBuddyLanguage>()
            .HasKey(l => new { l.StudyBuddyProfileId, l.LanguageCode });

        builder.Entity<StudyBuddyLanguage>()
            .HasOne(l => l.StudyBuddyProfile)
            .WithMany(p => p.Languages)
            .HasForeignKey(l => l.StudyBuddyProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        // ----- StudyBuddyMatch (self-referencing via AppUser) -----
        builder.Entity<StudyBuddyMatch>()
            .HasOne(m => m.Requester)
            .WithMany(u => u.MatchesInitiated)
            .HasForeignKey(m => m.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyBuddyMatch>()
            .HasOne(m => m.Recipient)
            .WithMany(u => u.MatchesReceived)
            .HasForeignKey(m => m.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyBuddyMatch>()
            .HasOne(m => m.Subject)
            .WithMany()
            .HasForeignKey(m => m.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StudyBuddyMatch>()
            .HasOne(m => m.Course)
            .WithMany()
            .HasForeignKey(m => m.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StudyBuddyMatch>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_StudyBuddyMatch_NotSelf",
                "\"RequesterId\" <> \"RecipientId\""));

        // A cross-institution pairing can't be pinned to one school's course.
        builder.Entity<StudyBuddyMatch>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_StudyBuddyMatch_NoCrossInstitutionCourse",
                "NOT (\"IsCrossInstitution\" AND \"CourseId\" IS NOT NULL)"));

        // ----- StudyGroup / StudyGroupMember (many-to-many) -----
        builder.Entity<StudyGroup>()
            .HasOne(g => g.CreatedBy)
            .WithMany()
            .HasForeignKey(g => g.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyGroup>()
            .HasOne(g => g.HostInstitution)
            .WithMany()
            .HasForeignKey(g => g.HostInstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StudyGroup>()
            .HasOne(g => g.Course)
            .WithMany(c => c.StudyGroups)
            .HasForeignKey(g => g.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StudyGroup>()
            .HasOne(g => g.Subject)
            .WithMany()
            .HasForeignKey(g => g.SubjectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<StudyGroup>()
            .HasIndex(g => new { g.HostInstitutionId, g.Visibility });

        // "Open study groups on this subject, anywhere."
        builder.Entity<StudyGroup>()
            .HasIndex(g => new { g.SubjectId, g.Visibility });

        builder.Entity<StudyGroupMember>()
            .HasKey(m => new { m.StudyGroupId, m.UserId });

        builder.Entity<StudyGroupMember>()
            .HasOne(m => m.StudyGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.StudyGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudyGroupMember>()
            .HasOne(m => m.User)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ----- Review -----
        builder.Entity<Review>()
            .HasOne(r => r.Reviewer)
            .WithMany(u => u.ReviewsGiven)
            .HasForeignKey(r => r.ReviewerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Review>()
            .HasOne(r => r.Reviewee)
            .WithMany(u => u.ReviewsReceived)
            .HasForeignKey(r => r.RevieweeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Review>()
            .HasOne(r => r.RelatedShareRequest)
            .WithMany()
            .HasForeignKey(r => r.RelatedShareRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Review>()
            .HasOne(r => r.RelatedMatch)
            .WithMany()
            .HasForeignKey(r => r.RelatedMatchId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Review>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_Review_Rating_Range",
                "\"Rating\" BETWEEN 1 AND 5"));

        // Exactly one of RelatedShareRequestId / RelatedMatchId should be set.
        builder.Entity<Review>()
            .ToTable(t => t.HasCheckConstraint(
                "CK_Review_ExactlyOneRelation",
                "(\"RelatedShareRequestId\" IS NOT NULL)::int + (\"RelatedMatchId\" IS NOT NULL)::int = 1"));

        // ----- Chat -----
        builder.Entity<Conversation>()
            .HasOne(c => c.CreatedBy)
            .WithMany()
            .HasForeignKey(c => c.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Conversation>()
            .HasOne(c => c.StudyGroup)
            .WithMany()
            .HasForeignKey(c => c.StudyGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // The invariant that keeps a pair from ending up with two parallel
        // threads. Filtered, because group conversations have no key.
        builder.Entity<Conversation>()
            .HasIndex(c => c.DirectKey)
            .IsUnique()
            .HasFilter("\"DirectKey\" IS NOT NULL");

        builder.Entity<Conversation>()
            .HasIndex(c => c.LastMessageAt);

        builder.Entity<ConversationParticipant>()
            .HasKey(p => new { p.ConversationId, p.UserId });

        builder.Entity<ConversationParticipant>()
            .HasOne(p => p.Conversation)
            .WithMany(c => c.Participants)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ConversationParticipant>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Every conversation this user is in" — runs on every socket connect.
        builder.Entity<ConversationParticipant>()
            .HasIndex(p => new { p.UserId, p.LeftAt });

        builder.Entity<ChatMessage>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ChatMessage>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ChatMessage>()
            .HasOne(m => m.ReplyToMessage)
            .WithMany()
            .HasForeignKey(m => m.ReplyToMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        // The message-history query: one conversation, newest first.
        builder.Entity<ChatMessage>()
            .HasIndex(m => new { m.ConversationId, m.SentAt });

        // ----- Calls -----
        builder.Entity<Call>()
            .HasOne(c => c.Conversation)
            .WithMany()
            .HasForeignKey(c => c.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Call>()
            .HasOne(c => c.StartedBy)
            .WithMany()
            .HasForeignKey(c => c.StartedById)
            .OnDelete(DeleteBehavior.Restrict);

        // "Is there a live call here?" on every conversation open, plus the
        // ring-timeout sweep.
        builder.Entity<Call>()
            .HasIndex(c => new { c.ConversationId, c.Status, c.StartedAt });

        builder.Entity<CallParticipant>()
            .HasKey(p => new { p.CallId, p.UserId });

        builder.Entity<CallParticipant>()
            .HasOne(p => p.Call)
            .WithMany(c => c.Participants)
            .HasForeignKey(p => p.CallId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CallParticipant>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ----- Notification -----
        builder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead });
    }
}
