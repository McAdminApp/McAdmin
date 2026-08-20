pipeline {
    agent none

    environment {
        IMAGE_NAME = 'mcservermgmnt'
        APP_PORT   = '5700'
    }

    options {
        buildDiscarder(logRotator(numToKeepStr: '10'))
        timeout(time: 30, unit: 'MINUTES')
        disableConcurrentBuilds()
    }

    stages {

        stage('Checkout') {
            agent { label 'deb-slave01' }
            steps {
                checkout scm
            }
        }

        stage('Bygg image') {
            agent { label 'deb-slave01' }
            steps {
                // The context is the repository root, not Web/src — the web project
                // references the API project and both must sit inside the context.
                sh '''
                    docker build -f Dockerfile \
                        -t ${IMAGE_NAME}:${BUILD_NUMBER} \
                        -t ${IMAGE_NAME}:latest .
                '''
            }
        }

        stage('Export plugin API') {
            agent { label 'deb-slave01' }
            steps {
                // McAdminPlugins.dll is pulled out of the image build and archived, so
                // plugin authors can download it from the build and reference it in
                // their own project. The layers are already cached from the previous
                // stage, and the Jenkins node needs no .NET SDK installed.
                sh '''
                    rm -rf artifacts
                    DOCKER_BUILDKIT=1 docker build -f Dockerfile \
                        --target api \
                        --output type=local,dest=artifacts .
                '''
                archiveArtifacts artifacts: 'artifacts/McAdminPlugins.dll', fingerprint: true, allowEmptyArchive: false
            }
        }

        stage('Deploy') {
            agent { label 'deb-slave01' }
            steps {
                // RCON-losenordet ligger i Jenkins credentials och skickas in till
                // docker compose, som satter det som McServer__RconPassword i containern.
                withCredentials([
                    string(credentialsId: 'mcservermgmnt-rcon-password', variable: 'MC_RCON_PASSWORD')
                ]) {
                    // docker-compose.yml stays in the repository root. Compose derives
                    // the project name from the directory the file sits in, and that
                    // name prefixes the mcservermgmnt_data and _keys volumes. Move the
                    // file down into a subfolder and the deploy loses both the database
                    // and the auth keys.
                    sh 'docker compose up -d --force-recreate'
                }
            }
        }

        stage('Hälsokontroll') {
            agent { label 'deb-slave01' }
            steps {
                retry(5) {
                    sleep(time: 5, unit: 'SECONDS')
                    sh 'curl -sf --max-time 10 http://localhost:${APP_PORT}/login > /dev/null'
                }
                echo "Hälsokontroll OK – applikationen svarar på http://localhost:${APP_PORT}"
            }
        }

        stage('Städa gamla images') {
            agent { label 'deb-slave01' }
            steps {
                // Tar bort dinglande lager från tidigare byggen, inte de taggade imagerna.
                sh 'docker image prune -f || true'
            }
        }
    }

    post {
        success {
            echo "✓ Build #${BUILD_NUMBER} lyckades och är driftsatt."
        }
        failure {
            echo "✗ Build #${BUILD_NUMBER} misslyckades."
        }
    }
}
