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
                sh '''
                    docker build -f Dockerfile \
                        -t ${IMAGE_NAME}:${BUILD_NUMBER} \
                        -t ${IMAGE_NAME}:latest .
                '''
            }
        }

        stage('Deploy') {
            agent { label 'deb-slave01' }
            steps {
                sh 'docker compose up -d --force-recreate'
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
