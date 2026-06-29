-- SmartMeteo Station Seed Data
-- seed.sql

-- Insert one station
INSERT INTO Stations (name, location, latitude, longitude, elevation)
VALUES ('Kyiv Central Station', 'Kyiv, Ukraine', 50.4501, 30.5234, 179.00);

-- Insert three sensors for the station
INSERT INTO Sensors (station_id, type, model, installed_on)
VALUES 
(1, 'temperature', 'TMP36', '2026-01-01'),
(1, 'pressure', 'BMP280', '2026-01-01'),
(1, 'wind', 'Anemometer V2', '2026-01-01');

-- Insert 30 measurements (10 per sensor)
-- Temperature readings
INSERT INTO Measurements (station_id, sensor_id, timestamp, value, unit)
VALUES
(1, 1, '2026-06-01 08:00:00', 22.5, 'C'),
(1, 1, '2026-06-01 09:00:00', 23.1, 'C'),
(1, 1, '2026-06-01 10:00:00', 24.0, 'C'),
(1, 1, '2026-06-01 11:00:00', 25.2, 'C'),
(1, 1, '2026-06-01 12:00:00', 26.0, 'C'),
(1, 1, '2026-06-01 13:00:00', 26.5, 'C'),
(1, 1, '2026-06-01 14:00:00', 27.1, 'C'),
(1, 1, '2026-06-01 15:00:00', 27.8, 'C'),
(1, 1, '2026-06-01 16:00:00', 28.2, 'C'),
(1, 1, '2026-06-01 17:00:00', 27.9, 'C');

-- Pressure readings
INSERT INTO Measurements (station_id, sensor_id, timestamp, value, unit)
VALUES
(1, 2, '2026-06-01 08:00:00', 1012.3, 'hPa'),
(1, 2, '2026-06-01 09:00:00', 1012.1, 'hPa'),
(1, 2, '2026-06-01 10:00:00', 1011.8, 'hPa'),
(1, 2, '2026-06-01 11:00:00', 1011.5, 'hPa'),
(1, 2, '2026-06-01 12:00:00', 1011.2, 'hPa'),
(1, 2, '2026-06-01 13:00:00', 1010.9, 'hPa'),
(1, 2, '2026-06-01 14:00:00', 1010.7, 'hPa'),
(1, 2, '2026-06-01 15:00:00', 1010.5, 'hPa'),
(1, 2, '2026-06-01 16:00:00', 1010.3, 'hPa'),
(1, 2, '2026-06-01 17:00:00', 1010.1, 'hPa');

-- Wind readings
INSERT INTO Measurements (station_id, sensor_id, timestamp, value, unit)
VALUES
(1, 3, '2026-06-01 08:00:00', 5.2, 'm/s'),
(1, 3, '2026-06-01 09:00:00', 5.8, 'm/s'),
(1, 3, '2026-06-01 10:00:00', 6.1, 'm/s'),
(1, 3, '2026-06-01 11:00:00', 6.5, 'm/s'),
(1, 3, '2026-06-01 12:00:00', 6.9, 'm/s'),
(1, 3, '2026-06-01 13:00:00', 7.2, 'm/s'),
(1, 3, '2026-06-01 14:00:00', 7.5, 'm/s'),
(1, 3, '2026-06-01 15:00:00', 7.8, 'm/s'),
(1, 3, '2026-06-01 16:00:00', 8.0, 'm/s'),
(1, 3, '2026-06-01 17:00:00', 7.7, 'm/s');
