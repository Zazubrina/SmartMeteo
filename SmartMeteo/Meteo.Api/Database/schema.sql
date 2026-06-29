-- SmartMeteo Station Database Schema
-- schema.sql

CREATE TABLE Stations (
    station_id INT PRIMARY KEY AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL,
    location VARCHAR(255),
    latitude DECIMAL(9,6),
    longitude DECIMAL(9,6),
    elevation DECIMAL(6,2)
);

CREATE TABLE Sensors (
    sensor_id INT PRIMARY KEY AUTO_INCREMENT,
    station_id INT NOT NULL,
    type VARCHAR(50) NOT NULL,   -- e.g., 'temperature', 'wind', 'pressure'
    model VARCHAR(100),
    installed_on DATE,
    FOREIGN KEY (station_id) REFERENCES Stations(station_id)
        ON DELETE CASCADE
        ON UPDATE CASCADE
);

CREATE TABLE Measurements (
    measurement_id BIGINT PRIMARY KEY AUTO_INCREMENT,
    station_id INT NOT NULL,
    sensor_id INT NOT NULL,
    timestamp DATETIME NOT NULL,
    value DECIMAL(8,2) NOT NULL,
    unit VARCHAR(20),
    FOREIGN KEY (station_id) REFERENCES Stations(station_id)
        ON DELETE CASCADE
        ON UPDATE CASCADE,
    FOREIGN KEY (sensor_id) REFERENCES Sensors(sensor_id)
        ON DELETE CASCADE
        ON UPDATE CASCADE
);

DELIMITER //
CREATE FUNCTION ToFahrenheit(temperature DECIMAL(10,2))
RETURNS DECIMAL(10,2)
DETERMINISTIC
BEGIN
    RETURN (temperature * 1.8) + 32;
END//
DELIMITER ;