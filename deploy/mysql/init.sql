-- Runs once, when the MySQL volume is first created. The image has already created the
-- database and user from MYSQL_DATABASE / MYSQL_USER. Tables come from each service's EF migrations.
ALTER DATABASE academies CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
