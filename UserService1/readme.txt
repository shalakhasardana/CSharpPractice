…or create a new repository on the command line
echo "# BookMyShow" >> README.md
git init
git add README.md
git commit -m "first commit"
git branch -M main
git remote add origin https://github.com/shalakhasardana/BookMyShow.git
git push -u origin main
…or push an existing repository from the command line
git remote add origin https://github.com/shalakhasardana/BookMyShow.git
git branch -M main
git push -u origin main

sql queries
------------------------------------------------------------------------------------------------------------------------------------------------------------------
Seeds in master table
select * from cities

INSERT INTO cities (id, name, slug) VALUES
  (3, 'Kirkland', 'Kirkland')

  INSERT INTO theaters (id, city_id, name, address) VALUES
  (3, 3, 'Kirkland Creek Multiplex', '123 Main St, Kirkland, WA')


INSERT INTO auditoriums (id, theater_id, name) VALUES
  (4, 3, 'Screen A');

select * from auditoriums
SELECT * from shows


INSERT INTO shows (id, auditorium_id, movie_id, start_at, end_at, language, format, version) VALUES
  (107, 4, 3, '2025-08-19 11:00:00-07', '2025-09-15 12:54:00-07', 'English','2D', 1) 

  select * from show_prices

INSERT INTO show_prices (id, show_id, seat_type_id, price) VALUES
(1019, 107, 1, 100.99), 
(1020, 107, 2, 200.99), 
(1021, 107, 3, 300.99)

select * from booking.seats


INSERT INTO booking.seats (auditorium_id, seat_code, seat_type_id) VALUES
  (4,'A1',1),(4,'A2',1),(4,'A3',1),(4,'A4',1),
  (4,'A5',2),(4,'A6',2),
  (4,'B1',1),(4,'B2',1),(4,'B3',1),(4,'B4',1)
ON CONFLICT (auditorium_id, seat_code) DO NOTHING;


insert into catalog.show_shard_map(show_id, shard_id) values
  (107,1)


----------------------------------------------------------------------------------------------------------------------------------------------------------

create schema if not exists catalog;

create table if not exists catalog.shards (
  id            int primary key,  -- 0..N-1
  name          text not null,
  conn_string   text not null,
  is_active     boolean not null default true
);

-- canonical mapping (can be by theater, show, or city)
create table if not exists catalog.theater_shard_map (
  theater_id    bigint primary key,
  shard_id      int not null references catalog.shards(id)
);

-- optionally cache show->shard (precomputed, denormalized)
create table if not exists catalog.show_shard_map (
  show_id       bigint primary key,
  shard_id      int not null references catalog.shards(id)
);

insert into catalog.shards(id, name, conn_string) values
  (0,'shard-0','Host=localhost;Port=5432;Database=shard0;Username=useradmin;Password=Passw0rd!;'),
  (1,'shard-1','Host=localhost;Port=5432;Database=shard1;Username=useradmin;Password=Passw0rd!;')
on conflict (id) do nothing;


booking:

-- One-time: create schema
CREATE SCHEMA IF NOT EXISTS booking;

-- Static seat map (per auditorium)
CREATE TABLE IF NOT EXISTS booking.seats (
  id            BIGSERIAL PRIMARY KEY,
  auditorium_id BIGINT NOT NULL REFERENCES auditoriums(id) ON DELETE CASCADE,
  seat_code     TEXT   NOT NULL,              -- e.g., "B12"
  seat_type_id  BIGINT NOT NULL REFERENCES seat_types(id),
  UNIQUE (auditorium_id, seat_code)
);

-- Seat inventory per show (one row per seat *for that show*)
CREATE TABLE IF NOT EXISTS booking.show_seats (
  show_id       BIGINT NOT NULL REFERENCES shows(id) ON DELETE CASCADE,
  seat_id       BIGINT NOT NULL REFERENCES booking.seats(id) ON DELETE CASCADE,
  status        TEXT   NOT NULL CHECK (status IN ('AVAILABLE','HELD','BOOKED')),
  hold_id       UUID,
  price         NUMERIC(10,2) NOT NULL,       -- snapshot from show_prices
  version       INT NOT NULL DEFAULT 1,       -- optimistic bump
  PRIMARY KEY (show_id, seat_id)
);

-- Holds (temporary reservations)
CREATE TABLE IF NOT EXISTS booking.holds (
  id           UUID PRIMARY KEY,
  show_id      BIGINT NOT NULL REFERENCES shows(id) ON DELETE CASCADE,
  user_id      BIGINT,
  expires_at   TIMESTAMPTZ NOT NULL,
  status       TEXT NOT NULL CHECK (status IN ('ACTIVE','EXPIRED','CONFIRMED','CANCELLED')),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Bookings
CREATE TABLE IF NOT EXISTS booking.bookings (
  id            BIGSERIAL PRIMARY KEY,
  code          TEXT NOT NULL UNIQUE,         -- public code like ABC123
  show_id       BIGINT NOT NULL REFERENCES shows(id) ON DELETE CASCADE,
  user_id       BIGINT,
  status        TEXT NOT NULL CHECK (status IN ('PENDING','CONFIRMED','CANCELLED','REFUNDED')),
  amount_total  NUMERIC(10,2) NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  paid_at       TIMESTAMPTZ
);

-- Which seats are in the booking
CREATE TABLE IF NOT EXISTS booking.booking_seats (
  booking_id   BIGINT NOT NULL REFERENCES booking.bookings(id) ON DELETE CASCADE,
  show_id      BIGINT NOT NULL,
  seat_id      BIGINT NOT NULL REFERENCES booking.seats(id),
  price_paid   NUMERIC(10,2) NOT NULL,
  PRIMARY KEY (booking_id, seat_id)
);

// shard db

CREATE TABLE IF NOT EXISTS booking.booking_seats (
  booking_id   BIGINT NOT NULL REFERENCES booking.bookings(id) ON DELETE CASCADE,
  show_id      BIGINT NOT NULL,
  seat_id      BIGINT ,
  price_paid   NUMERIC(10,2) NOT NULL,
  PRIMARY KEY (booking_id, seat_id)
);

-- Helpful indexes
CREATE INDEX IF NOT EXISTS ix_show_seats_status ON booking.show_seats (show_id, status);
CREATE INDEX IF NOT EXISTS ix_holds_expiry      ON booking.holds (expires_at) WHERE status='ACTIVE';


-------------------------------------------------------------


booking/holding


It requires cross join between databases. we can’t join across PostgreSQL databases directly. Do it with FDW (postgres_fdw) so your shard can “see” the tables in the other DB(s), then run the same INSERT…SELECT with fully-qualified foreign tables.


--setup FDN from shard to movie db
CREATE EXTENSION IF NOT EXISTS postgres_fdw;

DROP SERVER IF EXISTS srv_moviedb CASCADE;
CREATE SERVER srv_moviedb
  FOREIGN DATA WRAPPER postgres_fdw
  OPTIONS (host 'localhost', dbname 'moviedb', port '5432');


  -- Map a user that has SELECT on moviedb.public and moviedb.booking tables
CREATE USER MAPPING FOR CURRENT_USER
  SERVER srv_moviedb
  OPTIONS (user 'useradmin', password 'Passw0rd!');

  CREATE SCHEMA IF NOT EXISTS ext_public;
  CREATE SCHEMA IF NOT EXISTS ext_booking;


-- Import ONLY the needed tables from moviedb.public
IMPORT FOREIGN SCHEMA public
  LIMIT TO (show_prices, shows)
  FROM SERVER srv_moviedb
  INTO ext_public;

  -- Import ONLY the needed table from moviedb.booking
IMPORT FOREIGN SCHEMA booking
  LIMIT TO (seats)
  FROM SERVER srv_moviedb
  INTO ext_booking;

select * from booking.show_seats

CREATE UNIQUE INDEX IF NOT EXISTS uq_show_seats ON booking.show_seats(show_id, seat_id);

INSERT INTO booking.show_seats (show_id, seat_id, price, status, version)
SELECT
  sp.show_id,
  s.id,
  sp.price,
  'AVAILABLE',
  0
FROM ext_public.show_prices sp
JOIN ext_public.shows sh
  ON sh.id = sp.show_id
JOIN ext_booking.seats s
  ON s.auditorium_id = sh.auditorium_id
 AND s.seat_type_id   = sp.seat_type_id
WHERE sp.show_id = 107
ON CONFLICT (show_id, seat_id) DO NOTHING; 


user :

-- ===== Tables (public schema) ==============================================

-- Users(id, email, password, phone, status, created_at)
CREATE TABLE IF NOT EXISTS users (
  id          BIGSERIAL PRIMARY KEY,
  email       TEXT NOT NULL UNIQUE,
  password    TEXT NOT NULL,              -- store a bcrypt/argon2 hash
  phone       TEXT,
  status      TEXT NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','DISABLED')),
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Roles(id, name)
CREATE TABLE IF NOT EXISTS roles (
  id    SMALLSERIAL PRIMARY KEY,
  name  TEXT NOT NULL UNIQUE             -- e.g., Admin, Theatre_Manager, Customer
);

-- Permissions(id, name)
CREATE TABLE IF NOT EXISTS permissions (
  id    SMALLSERIAL PRIMARY KEY,
  name  TEXT NOT NULL UNIQUE             -- e.g., Theater_Write, Cancel_Show
);

-- RolePermissions(role_id, permission_id)
CREATE TABLE IF NOT EXISTS role_permissions (
  role_id        SMALLINT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
  permission_id  SMALLINT NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
  PRIMARY KEY (role_id, permission_id)
);

-- UserRoles(user_id, role_id)
CREATE TABLE IF NOT EXISTS user_roles (
  user_id  BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role_id  SMALLINT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
  PRIMARY KEY (user_id, role_id)
);

-- ===== Helpful indexes ======================================================
CREATE INDEX IF NOT EXISTS idx_users_email        ON users (email);
CREATE INDEX IF NOT EXISTS idx_userroles_user     ON user_roles (user_id);
CREATE INDEX IF NOT EXISTS idx_userroles_role     ON user_roles (role_id);
CREATE INDEX IF NOT EXISTS idx_roleperms_role     ON role_permissions (role_id);
CREATE INDEX IF NOT EXISTS idx_roleperms_perm     ON role_permissions (permission_id);

-- ===== Seed: Roles ==========================================================
INSERT INTO roles (name) VALUES
  ('Admin'),
  ('Theatre_Manager'),
  ('Customer')
ON CONFLICT (name) DO NOTHING;

-- ===== Seed: Permissions ====================================================
INSERT INTO permissions (name) VALUES
  ('Theater_Read'),
  ('Theater_Write'),
  ('Create_Show'),
  ('Update_Show'),
  ('Cancel_Show'),
  ('Book_Ticket'),
  ('Cancel_Booking'),
  ('View_Booking'),
  ('Manage_Users')
ON CONFLICT (name) DO NOTHING;

-- ===== Map Role → Permissions ==============================================

-- Admin → all permissions
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r CROSS JOIN permissions p
WHERE r.name = 'Admin'
ON CONFLICT DO NOTHING;

-- Theatre_Manager → theatre/show mgmt + view bookings
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p
  ON p.name IN ('Theater_Read','Theater_Write','Create_Show','Update_Show','Cancel_Show','View_Booking')
WHERE r.name = 'Theatre_Manager'
ON CONFLICT DO NOTHING;

-- Customer → booking actions
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
JOIN permissions p
  ON p.name IN ('Book_Ticket','Cancel_Booking','View_Booking')
WHERE r.name = 'Customer'
ON CONFLICT DO NOTHING;

-- ===== Seed: Users (use real bcrypt hashes!) ================================
-- Replace the password values below with real bcrypt hashes from your app.
INSERT INTO users (email, password, phone, status) VALUES
  ('admin@example.com',    '$2b$10$REPLACE_WITH_BCRYPT', NULL, 'ACTIVE'),
  ('manager@theatre.com',  '$2b$10$REPLACE_WITH_BCRYPT', NULL, 'ACTIVE'),
  ('customer1@example.com','$2b$10$REPLACE_WITH_BCRYPT', NULL, 'ACTIVE')
ON CONFLICT (email) DO NOTHING;

-- Assign roles to users
INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id FROM users u JOIN roles r ON r.name='Admin'
WHERE u.email='admin@example.com' ON CONFLICT DO NOTHING;

INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id FROM users u JOIN roles r ON r.name='Theatre_Manager'
WHERE u.email='manager@theatre.com' ON CONFLICT DO NOTHING;

INSERT INTO user_roles (user_id, role_id)
SELECT u.id, r.id FROM users u JOIN roles r ON r.name='Customer'
WHERE u.email='customer1@example.com' ON CONFLICT DO NOTHING;

-- ===== Convenience view: effective permissions per user =====================
CREATE OR REPLACE VIEW v_user_permissions AS
SELECT
  u.id           AS user_id,
  u.email        AS email,
  r.name         AS role,
  p.name         AS permission
FROM users u
JOIN user_roles ur        ON ur.user_id = u.id
JOIN roles r              ON r.id = ur.role_id
JOIN role_permissions rp  ON rp.role_id = r.id
JOIN permissions p        ON p.id = rp.permission_id
ORDER BY u.id, r.name, p.name;


------------------updated--------

-- ==== 1) Schema =============================================================
CREATE SCHEMA IF NOT EXISTS auth;

-- ==== 2) Core Tables ========================================================

-- Users
CREATE TABLE IF NOT EXISTS auth.users (
  id              BIGSERIAL PRIMARY KEY,
  email           CITEXT UNIQUE NOT NULL,
  full_name       TEXT NOT NULL,
  phone           TEXT UNIQUE,
  password_hash   TEXT NOT NULL,          -- store bcrypt hash from your app
  is_active       BOOLEAN NOT NULL DEFAULT TRUE,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Roles (Admin, Theatre_Manager, Customer)
CREATE TABLE IF NOT EXISTS auth.roles (
  id          SMALLSERIAL PRIMARY KEY,
  code        TEXT UNIQUE NOT NULL,       -- e.g., ADMIN, THEATRE_MANAGER, CUSTOMER
  name        TEXT NOT NULL,
  description TEXT
);

-- Permissions (fine-grained capabilities)
CREATE TABLE IF NOT EXISTS auth.permissions (
  id          SMALLSERIAL PRIMARY KEY,
  code        TEXT UNIQUE NOT NULL,       -- e.g., THEATER_WRITE, CANCEL_SHOW
  name        TEXT NOT NULL,
  description TEXT
);

-- Role ↔ Permission mapping (many-to-many)
CREATE TABLE IF NOT EXISTS auth.role_permissions (
  role_id       SMALLINT NOT NULL REFERENCES auth.roles(id) ON DELETE CASCADE,
  permission_id SMALLINT NOT NULL REFERENCES auth.permissions(id) ON DELETE CASCADE,
  PRIMARY KEY (role_id, permission_id)
);

-- User ↔ Role mapping (many-to-many)
CREATE TABLE IF NOT EXISTS auth.user_roles (
  user_id BIGINT NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
  role_id SMALLINT NOT NULL REFERENCES auth.roles(id) ON DELETE CASCADE,
  PRIMARY KEY (user_id, role_id)
);

-- ==== 3) Helpful Triggers (updated_at) =====================================
CREATE OR REPLACE FUNCTION auth.set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at := now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_users_updated_at ON auth.users;
CREATE TRIGGER trg_users_updated_at
BEFORE UPDATE ON auth.users
FOR EACH ROW EXECUTE FUNCTION auth.set_updated_at();

-- ==== 4) Indexes ============================================================
-- Speed up lookups
CREATE INDEX IF NOT EXISTS idx_users_email ON auth.users (email);
CREATE INDEX IF NOT EXISTS idx_user_roles_user ON auth.user_roles (user_id);
CREATE INDEX IF NOT EXISTS idx_user_roles_role ON auth.user_roles (role_id);
CREATE INDEX IF NOT EXISTS idx_role_perms_role ON auth.role_permissions (role_id);
CREATE INDEX IF NOT EXISTS idx_role_perms_perm ON auth.role_permissions (permission_id);

-- ==== 5) Seed Roles =========================================================
INSERT INTO auth.roles (code, name, description) VALUES
  ('ADMIN',            'Admin',            'Full system access'),
  ('THEATRE_MANAGER',  'Theatre Manager',  'Manage theatres and shows'),
  ('CUSTOMER',         'Customer',         'End user who books tickets')
ON CONFLICT (code) DO NOTHING;

-- ==== 6) Seed Permissions ===================================================
-- Adjust this list as your system grows
INSERT INTO auth.permissions (code, name, description) VALUES
  ('THEATER_READ',     'Theater Read',        'View theater details'),
  ('THEATER_WRITE',    'Theater Write',       'Create/update theater details'),
  ('CREATE_SHOW',      'Create Show',         'Create new shows'),
  ('UPDATE_SHOW',      'Update Show',         'Edit existing shows'),
  ('CANCEL_SHOW',      'Cancel Show',         'Cancel an existing show'),
  ('BOOK_TICKET',      'Book Ticket',         'Create a new booking'),
  ('CANCEL_BOOKING',   'Cancel Booking',      'Cancel an existing booking'),
  ('VIEW_BOOKING',     'View Booking',        'View booking details'),
  ('MANAGE_USERS',     'Manage Users',        'Create/update users and roles')
ON CONFLICT (code) DO NOTHING;

-- ==== 7) Map Role → Permissions ============================================
-- Admin gets all permissions
INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM auth.roles r
JOIN auth.permissions p ON TRUE
WHERE r.code = 'ADMIN'
ON CONFLICT DO NOTHING;

-- Theatre Manager: manage theatres/shows, see bookings
INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM auth.roles r
JOIN auth.permissions p ON p.code IN (
  'THEATER_READ','THEATER_WRITE',
  'CREATE_SHOW','UPDATE_SHOW','CANCEL_SHOW',
  'VIEW_BOOKING'
)
WHERE r.code = 'THEATRE_MANAGER'
ON CONFLICT DO NOTHING;

-- Customer: book/cancel & view their bookings
INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM auth.roles r
JOIN auth.permissions p ON p.code IN (
  'BOOK_TICKET','CANCEL_BOOKING','VIEW_BOOKING'
)
WHERE r.code = 'CUSTOMER'
ON CONFLICT DO NOTHING;

-- ==== 8) Dummy Users ========================================================
-- Replace the password_hash placeholders with real bcrypt hashes from your app.
-- e.g., using BCrypt.Net in C#: var hash = BCrypt.Net.BCrypt.HashPassword("Passw0rd!");
INSERT INTO auth.users (email, full_name, phone, password_hash) VALUES
  ('admin@example.com',          'System Admin',        NULL, '$2b$10$REPLACE_WITH_REAL_BCRYPT_HASH'),
  ('manager@theatre.com',        'Riya Manager',        NULL, '$2b$10$REPLACE_WITH_REAL_BCRYPT_HASH'),
  ('customer1@example.com',      'Arjun Customer',      NULL, '$2b$10$REPLACE_WITH_REAL_BCRYPT_HASH'),
  ('customer2@example.com',      'Meera Customer',      NULL, '$2b$10$REPLACE_WITH_REAL_BCRYPT_HASH')
ON CONFLICT (email) DO NOTHING;

-- ==== 9) Assign Roles to Users =============================================
INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u JOIN auth.roles r ON r.code='ADMIN'           WHERE u.email='admin@example.com'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u JOIN auth.roles r ON r.code='THEATRE_MANAGER' WHERE u.email='manager@theatre.com'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u JOIN auth.roles r ON r.code='CUSTOMER'        WHERE u.email='customer1@example.com'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u JOIN auth.roles r ON r.code='CUSTOMER'        WHERE u.email='customer2@example.com'
ON CONFLICT DO NOTHING;

-- ==== 10) Convenience View: Effective Permissions per User ==================
CREATE OR REPLACE VIEW auth.v_user_permissions AS
SELECT
  u.id         AS user_id,
  u.email      AS user_email,
  r.code       AS role_code,
  p.code       AS permission_code
FROM auth.users u
JOIN auth.user_roles ur        ON ur.user_id = u.id
JOIN auth.roles r              ON r.id = ur.role_id
JOIN auth.role_permissions rp  ON rp.role_id = r.id
JOIN auth.permissions p        ON p.id = rp.permission_id
ORDER BY u.id, r.code, p.code;

-- Example query:
-- SELECT * FROM auth.v_user_permissions WHERE user_email='manager@theatre.com';



Migrate to service fabric:
1. use any existing web appproject.
publishto exe: dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true