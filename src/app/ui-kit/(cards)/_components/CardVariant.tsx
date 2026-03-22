import React from "react";
import { Card, CardBody, Col } from "reactstrap";
import {
  IconAlarm,
  IconAward,
  IconBriefcase,
  IconBug,
} from "@tabler/icons-react";

type CardItem = {
  id: string;
  className: string;
  icon?: JSX.Element;
  heading: string;
  content: string;
  headingAfterText?: boolean;
};

const cardData: CardItem[] = [
  {
    id: "icon-card-1",
    className: "hover-effect card-primary",
    icon: <IconAlarm size={50} className="icon-bg text-light" />,
    heading: "Card With icon",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
  },
  {
    id: "icon-card-2",
    className: "hover-effect card-secondary",
    icon: <IconBug size={50} className="icon-bg text-light" />,
    heading: "Card With icon",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
  },
  {
    id: "icon-card-3",
    className: "hover-effect card-light-primary",
    icon: <IconBriefcase size={50} className="icon-bg" />,
    heading: "Card With icon",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
  },
  {
    id: "icon-card-4",
    className: "hover-effect card-light-secondary",
    icon: <IconAward size={50} className="icon-bg" />,
    heading: "Card With icon",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
  },
  {
    id: "border-card-1",
    className: "hover-effect border-primary border-top border-4",
    heading: "Card With Top border",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
  },
  {
    id: "border-card-2",
    className: "hover-effect border-secondary border-bottom border-4",
    heading: "Card With Bottom border",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
    headingAfterText: true,
  },
  {
    id: "border-card-3",
    className: "hover-effect border-success border-start border-4",
    heading: "Card With left border",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
    headingAfterText: true,
  },
  {
    id: "border-card-4",
    className: "hover-effect border-danger border-end border-4",
    heading: "Card With right border",
    content:
      "With supporting text below lead-in to additional content below as a natural.",
    headingAfterText: true,
  },
];

const CardVariant: React.FC = () => {
  return (
    <>
      {cardData.map(
        ({ id, className, icon, heading, content, headingAfterText }) => (
          <Col md="6" xl="3" key={id}>
            <Card className={className}>
              <CardBody>
                {icon}
                {!headingAfterText && <h6>{heading}</h6>}
                <p>{content}</p>
                {headingAfterText && <h6 className="mb-0 mt-2">{heading}</h6>}
              </CardBody>
            </Card>
          </Col>
        )
      )}
    </>
  );
};

export default CardVariant;
