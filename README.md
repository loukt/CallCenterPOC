# CallCenterPOC

## Introduction
CallCenterPOC is a proof-of-concept project written in C# that demonstrates the implementation of a call center application. This project is divided into two main parts: CallCenterCoreAPI and CallCenterPOC-App.

## Repository Structure
- **CallCenterCoreAPI**: This folder contains the core API for the call center application.
  - `CallCenterCoreAPI.csproj`: Project file for the API.
  - `Controllers`: Contains the controllers for handling API requests.
  - `Models`: Contains the data models used in the application.
  - `Program.cs`: The main entry point for the API.
  - `Properties`: Contains project properties.
  - `Services`: Contains service classes for business logic.
  - `appsettings.json`: Configuration file for the API.
  - `wwwroot`: Contains static files for the API.

- **CallCenterPOC-App**: This folder contains the application part of the project.
  - `CallCenterPOC-App.csproj`: Project file for the application.
  - `Pages`: Contains the Razor pages for the application.
  - `Program.cs`: The main entry point for the application.
  - `Properties`: Contains project properties.
  - `appsettings.Development.json`: Configuration file for development environment.
  - `appsettings.json`: Configuration file for the application.
  - `wwwroot`: Contains static files for the application.

## Installation
To install and run this project locally, follow these steps:

1. **Clone the repository:**
   ```sh
   git clone https://github.com/loukt/CallCenterPOC.git
   cd CallCenterPOC
   ```

2. **Build the projects:**
   Open the solution in your preferred IDE (e.g., Visual Studio) and build both `CallCenterCoreAPI` and `CallCenterPOC-App`.

3. **Run the projects:**
   Execute both the API and the application using your IDE or command line.

## Usage
Describe how to use the application once it is up and running. Include details about available features and functionalities.

## Contributing
If you would like to contribute to this project, please follow these guidelines:

1. Fork the repository.
2. Create a new branch (`git checkout -b feature/YourFeature`).
3. Commit your changes (`git commit -m 'Add some feature'`).
4. Push to the branch (`git push origin feature/YourFeature`).
5. Open a pull request.

## License
This project is licensed under the MIT License. See the LICENSE file for more details.

Feel free to customize and expand this template as needed to better fit your project's specifics. Let me know if you need any more details or modifications.
