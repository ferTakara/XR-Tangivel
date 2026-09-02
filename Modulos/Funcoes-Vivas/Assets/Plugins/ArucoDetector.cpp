#if !defined(__ANDROID__)
#include <opencv2/opencv.hpp>
#include <opencv2/objdetect.hpp>
#include <opencv2/objdetect/aruco_dictionary.hpp>
#include <opencv2/objdetect/aruco_detector.hpp>
#include <vector>

extern "C" {
    __declspec(dllexport) int DetectarAruco(
        unsigned char* pixels, 
        int width,
        int height,
        int channels,
        float* outCorners 
    ) {
        cv::Mat gray;
        if (channels == 4) {
            // No Editor (XR Simulation), a imagem vem com 4 canais (RGBA)
            cv::Mat rgba(height, width, CV_8UC4, pixels);
            cv::cvtColor(rgba, gray, cv::COLOR_RGBA2GRAY);
        } else {
            // No celular, o plano Y já é Grayscale (1 canal)
            gray = cv::Mat(height, width, CV_8UC1, pixels);
        }

        std::vector<int> markerIds;
        std::vector<std::vector<cv::Point2f>> markerCorners;

        // 2. Prepara o dicionário nas versões novas (4.7+)
        cv::aruco::Dictionary dictionary = cv::aruco::getPredefinedDictionary(cv::aruco::DICT_6X6_1000);
        cv::aruco::DetectorParameters detectorParams = cv::aruco::DetectorParameters();
        cv::aruco::ArucoDetector detector(dictionary, detectorParams);

        // 3. O OpenCV faz o trabalho pesado de achar o ArUco
        detector.detectMarkers(gray, markerCorners, markerIds);

        // 4. Se encontrou algum marcador...
        if (markerIds.size() > 0) {
            // Copiamos os 4 cantos do marcador para o array que o Unity nos enviou
            for (int i = 0; i < 4; i++) {
                outCorners[i * 2] = markerCorners[0][i].x;
                outCorners[i * 2 + 1] = markerCorners[0][i].y;
            }
            return markerIds[0]; // Retorna o ID do marcador (ex: 42)
        }

        return -1; // Retorna -1 se não achou nada
    }
}
#endif
