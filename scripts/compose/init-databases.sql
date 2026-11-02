-- One logical database per owning service (architecture-standards P3). The names are the services' own and the AppHost's.
CREATE DATABASE authdb;
CREATE DATABASE collectionsdb;
CREATE DATABASE environmentsdb;
CREATE DATABASE executiondb;
