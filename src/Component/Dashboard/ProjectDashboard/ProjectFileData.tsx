import React, { useEffect, useState } from "react";
import { Card, CardBody, Col, Row } from "reactstrap";
import { ProjectChartData } from "@/Data/Charts/ApexCharts/ApexChart";
import "filepond/dist/filepond.min.css";

const ProjectFileData = () => {
  const [Chart, setChart] = useState<any>(null);
  const [FilePond, setFilePond] = useState<any>(null);

  useEffect(() => {
    const loadModules = async () => {
      const [chartModule, filePondModule] = await Promise.all([
        import("react-apexcharts"),
        import("react-filepond"),
      ]);

      setChart(() => chartModule.default || chartModule);
      setFilePond(() => filePondModule.FilePond || filePondModule.default);
    };

    if (typeof window !== "undefined") {
      loadModules();
    }
  }, []);
  return (
    <Col md="6" xxl="4">
      <Row>
        <Col xs={12} className="mb-4">
          {FilePond ? (
            <FilePond
              className="filepond--root file-uploader-box filelight file-light-info filepond--hopper"
              id="fileUploaderBox"
              allowReorder={true}
              allowMultiple={true}
              labelIdle={`
                <img src="/images/dashboard/project/emoji.gif" alt="gif" class="w-40">
                <div class="filepond--label-action text-decoration-none">
                  <h5 class="text-info f-s-22">No Files Available!</h5>
                  <p class="text-dark f-s-14">Unfortunately, there's no open files right now</p>
                </div>
            `}
            />
          ) : (
            <div>Loading FilePond...</div>
          )}
        </Col>
        <Col xs={12}>
          <Card>
            <CardBody>
              {Chart ? (
                <Chart
                  options={ProjectChartData}
                  series={ProjectChartData.series}
                  type="line"
                  height={210}
                />
              ) : (
                <div>Loading Chart...</div>
              )}
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Col>
  );
};

export default ProjectFileData;
