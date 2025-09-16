Open http://localhost:5050
 with admin@example.com / admin.

Add your Postgres server in pgAdmin

Since pgAdmin runs in a container, connect via the host-mapped port:

In pgAdmin: Servers → Register → Server…
General → Name: Local Docker

Connection tab:

Host: host.docker.internal

Port: 5432

Maintenance DB: postgres

Username: postgres

Password: postgres

If you’re using pgAdmin desktop app (not container), use Host = localhost.

Create the required tables (if you haven’t yet)

Open users_db → Query Tool and run:

CREATE TABLE IF NOT EXISTS users (
  id BIGSERIAL PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  password_hash TEXT NOT NULL,
  phone TEXT,
  status TEXT NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','DISABLED')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS roles (
  id SMALLSERIAL PRIMARY KEY,
  code TEXT UNIQUE NOT NULL,
  name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS user_roles (
  user_id BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  role_id SMALLINT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
  PRIMARY KEY(user_id, role_id)
);


API: post: http://localhost:8081/api/users

Body: {
  "Email": "alice@example.com",
  "Password": "Test123!",
  "Phone": "4251234567"
}


echo "# BookMyShowSF" >> README.md
git init
git add README.md
git commit -m "first commit"
git branch -M main
git remote add origin https://github.com/shalakhasardana/BookMyShowSF.git
git push -u origin main


shalakhasardana18@gmail.com- objectid:1032dfe7-1696-485d-9abd-444bb5917d6d


for uers service db, run following command for postgresssetup:

# 1) Start / recreate a clean Postgres 16 container
docker rm -f pg-users 2>$null
docker run -d --name pg-users `
  -p 5432:5432 `
  -e POSTGRES_PASSWORD=postgres `
  -e POSTGRES_DB=users_db `
  postgres:16

# 2) (Optional) Start pgAdmin on http://localhost:5051 for GUI
docker rm -f bms-pgadmin 2>$null
docker run -d --name bms-pgadmin `
  -p 5051:80 `
  -e PGADMIN_DEFAULT_EMAIL=admin@local.test `
  -e PGADMIN_DEFAULT_PASSWORD=admin `
  dpage/pgadmin4:8


Create the table your code expects

Your repo queries users – if it doesn’t exist you’ll get 42P01. Create it once:

# Exec a psql session into the container
docker exec -it pg-users psql -U postgres -d users_db -c `
"CREATE TABLE IF NOT EXISTS users (
  id BIGSERIAL PRIMARY KEY,
  email TEXT UNIQUE NOT NULL,
  password_hash TEXT NOT NULL,
  phone TEXT,
  status TEXT NOT NULL DEFAULT 'ACTIVE'
);"

Make sure your app points at this DB

Your ServiceManifest already sets environment variables:

Database__Host=localhost
Database__Port=5432
Database__User=postgres
Database__Password=postgres
Database__Name=users_db
Database__SslMode=Disable

That’s correct for a local Service Fabric process talking to the Docker container (the container is mapped to host 5432). If you changed any values in code/config, undo them or ensure the same values.

Sanity checks
# Is postgres listening?
docker ps --filter "name=pg-users"

# Is port 5432 open on host?
netstat -ano | findstr :5432


If docker ps shows the container Up and netstat shows LISTENING, your service should be able to OpenAsync().


azure postgress password

Login:pgadmin 
Password:Test1@123456

