// The Api* entity duplicates that used to live here were removed: the cloud database now
// maps the same ExpenseTracker.Domain.Entities types as the device replica, with
// ApiDbContext choosing which properties apply. See ISyncEntity for why the entities carry
// the union of both schemas.
