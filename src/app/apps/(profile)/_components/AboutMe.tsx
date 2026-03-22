import React from "react";
import {
  IconBrandGithub,
  IconBriefcase,
  IconCake,
  IconDeviceLaptop,
  IconMail,
  IconMapPin,
  IconPhone,
} from "@tabler/icons-react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";
const aboutItems = [
  {
    icon: <IconBriefcase size={18} />,
    label: "Work passion",
    value: "IT Section",
  },
  {
    icon: <IconMail size={18} />,
    label: "Email",
    value: "Ninfa@gmail.com",
  },
  {
    icon: <IconPhone size={18} />,
    label: "Contact",
    value: "0364 4559103",
  },
  {
    icon: <IconCake size={18} />,
    label: "Birth of Date",
    value: "24 Oct",
  },
  {
    icon: <IconMapPin size={18} />,
    label: "Location",
    value: "Via Partenope, 117",
  },
  {
    icon: <IconDeviceLaptop size={18} />,
    label: "Website",
    value: "Ninfa_devWWW.com",
  },
  {
    icon: <IconBrandGithub size={18} />,
    label: "Github",
    value: "Ninfa_dev",
  },
];
const AboutMe = () => {
  return (
    <Card>
      <CardHeader>
        <h5>About Me</h5>
      </CardHeader>
      <CardBody>
        <p className="text-muted f-s-13">
          Hello! I am,Ninfa Monaldo Devoted web designer with over five years of
          experience and a strong understanding of Adobe Creative Suite, HTML5,
          CSS3 and Java. Excited to bring my exceptional front-end development
          abilities to the retail industry.{" "}
        </p>
        <div className="about-list">
          {aboutItems.map((item, idx) => (
            <Row key={idx} className="mb-2 align-items-center">
              <Col xs="6">
                <span className="fw-semibold d-flex align-items-center gap-2">
                  {item.icon} {item.label}
                </span>
              </Col>
              <Col xs="6" className="text-end text-secondary small">
                {item.value}
              </Col>
            </Row>
          ))}
        </div>
      </CardBody>
    </Card>
  );
};

export default AboutMe;
